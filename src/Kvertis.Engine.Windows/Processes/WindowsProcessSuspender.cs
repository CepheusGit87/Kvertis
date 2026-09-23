using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Kvertis.Engine.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace Kvertis.Engine.Windows.Processes;

/// <summary>
/// Pauses a child process (ffmpeg) by suspending all of its threads (ADR-005).
/// </summary>
/// <remarks>
/// API status: <c>NtSuspendProcess</c>/<c>NtResumeProcess</c> are exported by ntdll.dll and have been stable
/// for many Windows releases, but they are <b>not documented</b> and could change. Every failure therefore
/// returns false, and the caller falls back to cancel-and-restart.
/// Phase-2 alternative using only documented APIs: start ffmpeg in a Job Object and suspend/resume each thread
/// via <c>CreateToolhelp32Snapshot</c> + <c>OpenThread(THREAD_SUSPEND_RESUME)</c> + <c>SuspendThread</c>/<c>ResumeThread</c>
/// (racy for threads created during the walk). <c>DebugActiveProcess</c> is rejected as too intrusive
/// (attaches a debugger; the target dies if the debugger exits without detaching).
/// Suspend counts nest: every successful <see cref="TrySuspend"/> needs a matching <see cref="TryResume"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessSuspender : IProcessSuspender
{
    private const uint ProcessSuspendResume = 0x0800;

    public bool TrySuspend(int processId) => Invoke(processId, suspend: true);

    public bool TryResume(int processId) => Invoke(processId, suspend: false);

    private static bool Invoke(int processId, bool suspend)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using var handle = OpenProcess(ProcessSuspendResume, false, (uint)processId);
            if (handle.IsInvalid)
            {
                return false;
            }

            var status = suspend ? NtSuspendProcess(handle) : NtResumeProcess(handle);
            return status >= 0; // NT_SUCCESS
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            return false;
        }
    }

    // DllImport instead of LibraryImport: the source-generated variant requires <AllowUnsafeBlocks>, which the
    // project does not enable. All signatures are blittable apart from SafeHandle/bool, which the runtime marshals.
    [DllImport("kernel32.dll", SetLastError = false, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("ntdll.dll", SetLastError = false, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtSuspendProcess(SafeProcessHandle processHandle);

    [DllImport("ntdll.dll", SetLastError = false, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtResumeProcess(SafeProcessHandle processHandle);
}
