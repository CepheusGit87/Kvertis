using ImageMagick;
using ImageMagick.Configuration;

namespace Kvertis.Engine.Conversion.Images;

/// <summary>
/// Locks ImageMagick down before the first image is touched: no URL/network coders, no external
/// delegates (external renderers and converters), no scripting coders (MSL/MVG/TEXT), and hard resource
/// limits so a crafted file cannot exhaust memory or disk. Kvertis has no network code; this makes
/// sure the bundled library cannot add any (e.g. an SVG with an external reference).
/// </summary>
public static class MagickSecurity
{
    private static readonly Lazy<bool> Initialized = new(Apply);

    /// <summary>Content of ImageMagick's policy.xml. Kept as a constant so tests can assert on it.</summary>
    public const string PolicyXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE policymap [
          <!ELEMENT policymap (policy)*>
          <!ELEMENT policy EMPTY>
          <!ATTLIST policy domain (delegate|coder|filter|path|resource|system|cache|module) #IMPLIED>
          <!ATTLIST policy name CDATA #IMPLIED>
          <!ATTLIST policy pattern CDATA #IMPLIED>
          <!ATTLIST policy rights CDATA #IMPLIED>
          <!ATTLIST policy stealth CDATA #IMPLIED>
          <!ATTLIST policy value CDATA #IMPLIED>
        ]>
        <policymap>
          <!-- No external programs, ever. -->
          <policy domain="delegate" rights="none" pattern="*" />
          <!-- No network or scripting coders. -->
          <policy domain="coder" rights="none" pattern="{URL,HTTPS,HTTP,FTP,MVG,MSL,TEXT,SHOW,WIN,PLT,PS,PS2,PS3,EPS,PDF,XPS}" />
          <!-- No reading from /proc, devices or pipes. -->
          <policy domain="path" rights="none" pattern="@*" />
          <policy domain="path" rights="none" pattern="|*" />
          <!-- Resource ceilings; conversions above these fail instead of thrashing the machine. -->
          <policy domain="resource" name="memory" value="2GiB" />
          <policy domain="resource" name="map" value="4GiB" />
          <policy domain="resource" name="width" value="64KP" />
          <policy domain="resource" name="height" value="64KP" />
          <policy domain="resource" name="area" value="1GP" />
          <policy domain="resource" name="disk" value="8GiB" />
          <policy domain="resource" name="time" value="600" />
          <policy domain="resource" name="list-length" value="256" />
        </policymap>
        """;

    /// <summary>Applies the policy once per process. Safe to call from every entry point.</summary>
    public static void EnsureInitialized() => _ = Initialized.Value;

    private static bool Apply()
    {
        var files = ConfigurationFiles.Default;
        files.Policy.Data = PolicyXml;
        MagickNET.Initialize(files);
        return true;
    }
}
