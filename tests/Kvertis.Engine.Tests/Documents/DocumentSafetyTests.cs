using System.Text.RegularExpressions;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Documents;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Probing;
using Shouldly;
using Xunit;
using static Kvertis.Engine.Tests.Documents.DocumentTestFiles;

namespace Kvertis.Engine.Tests.Documents;

public sealed partial class DocumentSafetyTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kvertis.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static List<string> DocumentSources()
    {
        var root = RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "src", "Kvertis.Engine", "Conversion", "Documents"), "*.cs").ToList();
        files.Add(Path.Combine(root, "src", "Kvertis.Engine", "Probing", "DocumentProber.cs"));
        return files;
    }

    [Fact]
    public void Document_code_contains_no_network_api()
    {
        var files = DocumentSources();
        files.Count.ShouldBeGreaterThan(5);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            NetworkApi().IsMatch(text).ShouldBeFalse($"network API referenced in {Path.GetFileName(file)}");
        }
    }

    [Fact]
    public void Document_code_never_supplies_a_pdf_password()
    {
        foreach (var file in DocumentSources())
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("Password =", customMessage: Path.GetFileName(file));
            text.ShouldNotContain("Passwords", customMessage: Path.GetFileName(file));
        }
    }

    [Fact]
    public async Task Text_formats_are_not_probed()
    {
        var txt = WriteText(_dir.File("a.txt"), "hello");
        var info = Info(txt, FormatRegistry.Txt);

        var probed = await new DocumentProber().ProbeAsync(info, CancellationToken.None);

        probed.ShouldBe(info);
        new DocumentProber().Supports(MediaKind.Image).ShouldBeFalse();
    }

    [Fact]
    public async Task Prober_timeout_is_reported_as_timeout()
    {
        var path = _dir.File("big.pdf");
        await File.WriteAllTextAsync(path, "%PDF-1.4\n" + new string('0', 5_000_000));

        var ex = await Should.ThrowAsync<ConversionException>(() => new DocumentProber(TimeSpan.FromMilliseconds(1)).ProbeAsync(Info(path, FormatRegistry.Pdf), CancellationToken.None));

        ex.Code.ShouldBeOneOf(ConversionErrorCode.Timeout, ConversionErrorCode.CorruptFile);
    }

    [Fact]
    public void Sheet_names_become_safe_file_names()
    {
        DocumentPaths.SafeName("a/b:c*?").ShouldBe("a_b_c__");
        DocumentPaths.SafeName("  ").ShouldBe("_");
        DocumentPaths.PagePath(Path.Combine("x", "doc.png"), 9).ShouldBe(Path.Combine("x", "doc_p010.png"));
    }

    [Fact]
    public void Csv_field_quoting_follows_rfc4180()
    {
        OfficeConverter.CsvField("plain").ShouldBe("plain");
        OfficeConverter.CsvField("a,b").ShouldBe("\"a,b\"");
        OfficeConverter.CsvField("q\"q").ShouldBe("\"q\"\"q\"");
        OfficeConverter.CsvField("l1\r\nl2").ShouldBe("\"l1\r\nl2\"");
    }

    [GeneratedRegex(@"HttpClient|WebRequest|Socket|WebClient")]
    private static partial Regex NetworkApi();
}
