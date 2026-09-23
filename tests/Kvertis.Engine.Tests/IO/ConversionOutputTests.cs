using Kvertis.Engine.Abstractions;
using Kvertis.Engine.IO;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.IO;

public sealed class ConversionOutputTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kvertis-tests", Guid.NewGuid().ToString("N"));

    public ConversionOutputTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public void Commit_moves_temp_onto_target()
    {
        var target = Path.Combine(_dir, "out.png");
        using var output = ConversionOutput.Begin(target, 0);
        File.WriteAllText(output.TempPath, "new");

        output.Commit().ShouldBe(3);

        File.ReadAllText(target).ShouldBe("new");
        File.Exists(output.TempPath).ShouldBeFalse();
    }

    [Fact]
    public void Target_that_appeared_during_conversion_is_not_overwritten()
    {
        var target = Path.Combine(_dir, "out.png");
        using var output = ConversionOutput.Begin(target, 0);
        File.WriteAllText(output.TempPath, "new");
        File.WriteAllText(target, "someone else's file");

        var ex = Should.Throw<ConversionException>(() => output.Commit());

        ex.Code.ShouldBe(ConversionErrorCode.OutputExists);
        File.ReadAllText(target).ShouldBe("someone else's file");
        File.Exists(output.TempPath).ShouldBeFalse();
    }

    [Fact]
    public void Overwrite_only_with_consent_from_begin()
    {
        var target = Path.Combine(_dir, "out.png");
        File.WriteAllText(target, "old");
        using var output = ConversionOutput.Begin(target, 0, allowOverwrite: true);
        File.WriteAllText(output.TempPath, "new");

        output.Commit();

        File.ReadAllText(target).ShouldBe("new");
    }

    [Fact]
    public void Existing_target_without_consent_is_refused_up_front()
    {
        var target = Path.Combine(_dir, "out.png");
        File.WriteAllText(target, "old");

        Should.Throw<ConversionException>(() => ConversionOutput.Begin(target, 0)).Code.ShouldBe(ConversionErrorCode.OutputExists);
    }
}
