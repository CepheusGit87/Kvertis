using ImageMagick;
using Kvertis.Engine.Conversion.Images;
using Xunit;
using Shouldly;

namespace Kvertis.Engine.Tests.Images;

public class MagickSecurityTests
{
    [Fact]
    public void Policy_forbids_delegates_and_network_coders()
    {
        MagickSecurity.PolicyXml.ShouldContain("domain=\"delegate\" rights=\"none\" pattern=\"*\"");
        MagickSecurity.PolicyXml.ShouldContain("URL,HTTPS,HTTP,FTP");
    }

    [Fact]
    public void Initialization_is_idempotent_and_blocks_url_reads()
    {
        MagickSecurity.EnsureInitialized();
        MagickSecurity.EnsureInitialized();

        // Reading through the URL coder must be refused by policy, regardless of the target.
        var settings = new MagickReadSettings { Format = MagickFormat.Https };
        Should.Throw<MagickException>(() => new MagickImage("https://localhost/none.png", settings));
    }

    [Fact]
    public void Images_can_still_be_read_and_written_after_policy()
    {
        MagickSecurity.EnsureInitialized();
        using var image = new MagickImage(MagickColors.Blue, 8, 8);
        var bytes = image.ToByteArray(MagickFormat.Png);
        using var back = new MagickImage(bytes);
        back.Width.ShouldBe(8u);
    }
}
