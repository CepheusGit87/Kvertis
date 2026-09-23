using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Probing;

namespace Kvertis.Queue.Tests;

/// <summary>
/// Routing needs the cached ffprobe data (ADR-015). When it is gone before a job runs, the runner detects the input
/// again so an MKV with H.264 is resolved against fresh codec data instead of falling to the ffmpeg converter.
/// </summary>
public sealed class RedetectionTests
{
    private static readonly MediaInfo H264 = new(TimeSpan.FromSeconds(60), 1920, 1080, "h26" + "4", "aa" + "c", true, true, 1, false, false, false)
    {
        StreamCodecs = ["h26" + "4", "aa" + "c"],
    };

    [Fact]
    public async Task Missing_probe_data_is_detected_again_before_the_converter_is_resolved()
    {
        var cache = new MediaInfoCache();
        var detector = Substitute.For<IFormatDetector>();
        await using var h = new QueueHarness(detector: detector, mediaInfo: cache);
        var job = h.NewJob("clip.mkv", MediaKind.Video, "mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(job.Input.Path)!);
        await File.WriteAllBytesAsync(job.Input.Path, new byte[64]);
        var redetected = job.Input with { Warnings = [InputWarning.MetadataNotStrippable] };
        detector.DetectAsync(job.Input.Path, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cache.Set(job.Input.Path, H264); // the real detector probes and fills the cache
            return Task.FromResult(redetected);
        });

        h.Queue.Enqueue(job);
        var call = await h.NextCallAsync();

        await detector.Received(1).DetectAsync(job.Input.Path, Arg.Any<CancellationToken>());
        h.Resolver.Received(1).Resolve(redetected, job.Settings.Output);
        call.Input.ShouldBeSameAs(redetected);
        cache.TryGet(job.Input.Path, out _).ShouldBeTrue();
        call.Complete();
    }

    [Fact]
    public async Task Cached_probe_data_and_non_media_inputs_are_not_detected_again()
    {
        var cache = new MediaInfoCache();
        var detector = Substitute.For<IFormatDetector>();
        await using var h = new QueueHarness(detector: detector, mediaInfo: cache);
        var video = h.NewJob("clip.mkv", MediaKind.Video, "mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(video.Input.Path)!);
        await File.WriteAllBytesAsync(video.Input.Path, new byte[64]);
        cache.Set(video.Input.Path, H264);
        var image = h.NewJob("photo.jpg");

        h.Queue.EnqueueRange([video, image]);
        var first = await h.NextCallAsync();
        var second = await h.NextCallAsync();

        await detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, default);
        new[] { first.Input, second.Input }.ShouldBe([video.Input, image.Input], ignoreOrder: true);
        first.Complete();
        second.Complete();
    }
}
