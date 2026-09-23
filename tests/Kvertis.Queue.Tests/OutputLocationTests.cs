using Kvertis.Engine.Abstractions;

namespace Kvertis.Queue.Tests;

public sealed class OutputLocationTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Same_folder_is_the_input_folder()
    {
        var input = _temp.Combine("photos", "a.jpg");
        OutputDirectoryResolver.Resolve(input, OutputLocation.SameFolder).ShouldBe(_temp.Combine("photos"));
    }

    [Fact]
    public void Sub_folder_defaults_to_Kvertis()
    {
        var input = _temp.Combine("photos", "a.jpg");
        OutputDirectoryResolver.Resolve(input, OutputLocation.SubFolder()).ShouldBe(_temp.Combine("photos", "Kvertis"));
        OutputDirectoryResolver.Resolve(input, OutputLocation.SubFolder("Small")).ShouldBe(_temp.Combine("photos", "Small"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void Sub_folder_rejects_escaping_names(string name)
    {
        Should.Throw<ArgumentException>(() => OutputDirectoryResolver.Resolve(_temp.Combine("a.jpg"), OutputLocation.SubFolder(name)));
    }

    [Fact]
    public void Custom_folder_is_used_as_is()
    {
        var target = _temp.Combine("elsewhere");
        OutputDirectoryResolver.Resolve(_temp.Combine("a.jpg"), OutputLocation.Custom(target)).ShouldBe(target);
        Should.Throw<ArgumentException>(() => OutputDirectoryResolver.Resolve(_temp.Combine("a.jpg"), OutputLocation.Custom(" ")));
    }

    [Fact]
    public async Task Factory_detects_input_and_resolves_directory()
    {
        var path = _temp.Combine("song.wav");
        var detector = Substitute.For<IFormatDetector>();
        detector.DetectAsync(path, Arg.Any<CancellationToken>())
            .Returns(new InputInfo(path, new FormatId("wav"), MediaKind.Audio, 42, TimeSpan.FromSeconds(3), null, null, null, []));

        var job = await ConversionJobFactory.CreateAsync(detector, path, new ConversionSettings(new FormatId("mp3")), OutputLocation.SubFolder(), "{name}_{n}", batchIndex: 3);

        job.Input.Kind.ShouldBe(MediaKind.Audio);
        job.OutputDirectory.ShouldBe(_temp.Combine("Kvertis"));
        job.NamePattern.ShouldBe("{name}_{n}");
        job.BatchIndex.ShouldBe(3);
        job.State.ShouldBe(JobState.Queued);
    }
}
