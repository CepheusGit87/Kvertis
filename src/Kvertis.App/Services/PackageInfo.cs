using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace Kvertis.App.Services;

/// <summary>
/// Package identity of the running process. The Store build always has one (MSIX). A plain <c>dotnet build</c> with
/// <c>WindowsPackageType=None</c> starts the same code without identity; that is the developer loop without
/// developer mode, and it must not touch package-only APIs (<see cref="Package.Current"/>, <c>ApplicationData</c>).
/// </summary>
public static class PackageInfo
{
    private const int AppModelErrorNoPackage = 15700;

    /// <summary>True when the process runs from a registered MSIX package.</summary>
    public static bool HasIdentity { get; } = DetectIdentity();

    /// <summary>Version as "major.minor.build", from the package or, unpackaged, from the assembly.</summary>
    public static string VersionText
    {
        get
        {
            if (HasIdentity)
            {
                var v = Package.Current.Id.Version;
                return Join(v.Major, v.Minor, v.Build);
            }
            var a = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return Join(a.Major, a.Minor, Math.Max(a.Build, 0));
        }
    }

    private static string Join(int major, int minor, int build) =>
        string.Join('.', new[] { major, minor, build }.Select(v => v.ToString(CultureInfo.InvariantCulture)));

    private static bool DetectIdentity()
    {
        var length = 0;
        var result = GetCurrentPackageFullName(ref length, null);
        return result != AppModelErrorNoPackage;
    }

    // DllImport instead of LibraryImport: the source-generated variant requires <AllowUnsafeBlocks> (see
    // WindowsProcessSuspender in Kvertis.Engine.Windows).
#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
#pragma warning restore SYSLIB1054
}
