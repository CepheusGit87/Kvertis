using Kvertis.Engine.Naming;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Naming;

public sealed class OutputNamePatternTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    private static string Render(string inputName, string pattern = OutputNamePattern.Default) =>
        OutputNamePattern.Render(pattern, Path.Combine(Path.GetTempPath(), inputName), "mp3", Now);

    [Theory]
    [InlineData("CON.wav", "_CON.mp3")]
    [InlineData("con.wav", "_con.mp3")]
    [InlineData("Prn.wav", "_Prn.mp3")]
    [InlineData("aux.wav", "_aux.mp3")]
    [InlineData("NUL.wav", "_NUL.mp3")]
    [InlineData("com1.wav", "_com1.mp3")]
    [InlineData("LPT9.wav", "_LPT9.mp3")]
    [InlineData("nul.backup.wav", "_nul.backup.mp3")]
    public void Windows_device_names_get_a_prefix(string input, string expected)
    {
        Render(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("console.wav", "console.mp3")]
    [InlineData("COM10.wav", "COM10.mp3")]
    [InlineData("COM0.wav", "COM0.mp3")]
    [InlineData("LPT.wav", "LPT.mp3")]
    [InlineData("my con.wav", "my con.mp3")]
    public void Ordinary_names_are_unchanged(string input, string expected)
    {
        Render(input).ShouldBe(expected);
    }

    [Fact]
    public void Pattern_producing_a_device_name_is_prefixed_too()
    {
        Render("song.wav", "aux").ShouldBe("_aux.mp3");
    }
}
