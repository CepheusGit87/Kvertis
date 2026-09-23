using System.Diagnostics;
using System.Text;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Audio;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Platform;
using Kvertis.Engine.Probing;
using Kvertis.Engine.Processes;
using Kvertis.Engine.Tests.Ffmpeg;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Audio;

/// <summary>Skips itself when no ffmpeg/ffprobe pair is on PATH, so CI without binaries stays green.</summary>
public sealed class FfmpegOnPathFactAttribute : FactAttribute
{
    public FfmpegOnPathFactAttribute()
    {
        var locator = new FfmpegLocator();
        if (locator.FfmpegPath is null || locator.FfprobePath is null)
        {
            Skip = "ffmpeg/ffprobe not found";
        }
    }
}

[Trait("Category", "Integration")]
public class AudioIntegrationTests
{
    [FfmpegOnPathFact]
    public async Task ConvertsOneSecondWavToMp3WithRealFfmpeg()
    {
        var locator = new FfmpegLocator();
        var runner = new ProcessRunner();
        var tools = new FfmpegToolset(locator, runner, NullSystemCodecCapabilities.Instance, new FfmpegFeatureProbe(locator, runner),
            new FfmpegCompliance(locator, runner), new FfprobeReader(locator, runner), new MediaInfoCache(), new FormatRegistry());

        var report = await FfmpegCompliance.CheckAsync(locator, runner);
        if (!report.IsCompliant)
        {
            // Typical distribution builds are GPL: Kvertis must refuse them, which is what we can verify here.
            var refused = await Should.ThrowAsync<ConversionException>(() => tools.Compliance.EnsureCompliantAsync(CancellationToken.None));
            refused.Code.ShouldBe(ConversionErrorCode.ToolMissing);
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "kvertis-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var wav = Path.Combine(dir, "tone.wav");
            await File.WriteAllBytesAsync(wav, SineWav(seconds: 1));
            var detector = new FormatDetector(new FormatRegistry(), [new MediaProber(tools.Reader, locator, tools.Cache)]);
            var input = await detector.DetectAsync(wav, CancellationToken.None);
            input.Duration.ShouldNotBeNull();

            var output = Path.Combine(dir, "tone.mp3");
            var stopwatch = Stopwatch.StartNew();
            var result = await new AudioConverter(tools).ConvertAsync(input, output, new ConversionSettings(FormatRegistry.Mp3),
                new SyncProgress<ConversionProgress>(_ => { }), CancellationToken.None);

            result.OutputBytes.ShouldBeGreaterThan(1000);
            File.Exists(output).ShouldBeTrue();
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static byte[] SineWav(int seconds, int rate = 44100)
    {
        var samples = rate * seconds;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        w.Write("RIFF"u8);
        w.Write(36 + (samples * 2));
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
        }
        w.Flush();
        return ms.ToArray();
    }
}
