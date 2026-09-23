using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Documents;
using Kvertis.Engine.Formats;
using PdfSharp.Drawing;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Kvertis.Engine.Tests.Documents;

/// <summary>Temporary folder per test; removed on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kvertis-doc-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class NullProgress : IProgress<ConversionProgress>
{
    public List<ConversionProgress> Reports { get; } = [];

    public void Report(ConversionProgress value)
    {
        lock (Reports)
        {
            Reports.Add(value);
        }
    }
}

/// <summary>Builds a JPEG with a hand-made EXIF segment (orientation + artist), independent of encoder behavior.</summary>
internal static class ExifJpeg
{
    public static byte[] WithExif(byte[] jpeg, ushort orientation, string artist)
    {
        var artistBytes = System.Text.Encoding.ASCII.GetBytes(artist + "\0");
        using var tiff = new MemoryStream();
        using (var w = new BinaryWriter(tiff, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("II"u8.ToArray());
            w.Write((ushort)42);
            w.Write(8u); // IFD offset
            w.Write((ushort)2); // entries
            w.Write((ushort)0x0112); // Orientation, SHORT
            w.Write((ushort)3);
            w.Write(1u);
            w.Write(orientation);
            w.Write((ushort)0);
            w.Write((ushort)0x013B); // Artist, ASCII
            w.Write((ushort)2);
            w.Write((uint)artistBytes.Length);
            w.Write(8u + 2 + 2 * 12 + 4); // data right after the IFD
            w.Write(0u); // no next IFD
            w.Write(artistBytes);
        }
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff.ToArray()).ToArray();
        var length = payload.Length + 2;
        var segment = new byte[] { 0xFF, 0xE1, (byte)(length >> 8), (byte)length }.Concat(payload);
        return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray();
    }
}

internal static class DocumentTestFiles
{
    public static InputInfo Info(string path, FormatId format, MediaKind kind = MediaKind.Document) =>
        new(path, format, kind, new FileInfo(path).Length, null, null, null, null, []);

    public static ConversionSettings To(FormatId output, Dictionary<string, string>? advanced = null, MetadataPolicy metadata = MetadataPolicy.Strip) =>
        new(output, Metadata: metadata, Advanced: advanced);

    public static string WriteText(string path, string content)
    {
        System.IO.File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        return path;
    }

    // ---- PDF ---------------------------------------------------------------------------------

    public static string CreatePdf(string path, IReadOnlyList<string> pageTexts, Action<PdfSharp.Pdf.PdfDocument>? configure = null)
    {
        DocumentFonts.EnsureInitialized();
        using var document = new PdfSharp.Pdf.PdfDocument();
        var font = new XFont(DocumentFonts.Sans, 12, XFontStyleEx.Regular, new XPdfFontOptions(PdfSharp.Pdf.PdfFontEncoding.Unicode));
        foreach (var text in pageTexts)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString(text, font, XBrushes.Black, 72, 72, XStringFormats.TopLeft);
        }
        configure?.Invoke(document);
        document.Save(path);
        return path;
    }

    // ---- DOCX --------------------------------------------------------------------------------

    public static string CreateDocx(string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        // Localized style ids with the English built-in names, as a German word processor writes them.
        styles.Styles = new W.Styles(
            new W.Style(new W.StyleName { Val = "heading 1" }) { Type = W.StyleValues.Paragraph, StyleId = "berschrift1" },
            new W.Style(new W.StyleName { Val = "heading 2" }) { Type = W.StyleValues.Paragraph, StyleId = "berschrift2" });

        var link = main.AddHyperlinkRelationship(new Uri("https://example.org/page"), true);

        main.Document = new W.Document(new W.Body(
            Para("berschrift1", Run("Report Title")),
            Para(null, Run("Plain start "), Run("bold part", bold: true), Run(" and "), Run("italic part", italic: true), Run(".")),
            Para(null, Run("Second paragraph with *stars*")),
            Para("berschrift2", Run("Section")),
            ListPara(0, Run("First item")),
            ListPara(1, Run("Nested item")),
            ListPara(0, Run("Second item")),
            new W.Table(
                new W.TableRow(Cell("Name"), Cell("Value")),
                new W.TableRow(Cell("alpha"), Cell("1|2"))),
            new W.Paragraph(new W.Run(new W.Drawing())),
            new W.Paragraph(new W.Run(new W.Text("Visit "){ Space = SpaceProcessingModeValues.Preserve }),
                new W.Hyperlink(new W.Run(new W.Text("our site"))) { Id = link.Id }),
            Para(null, Run("<b>not html</b> & more"))));
        return path;
    }

    private static W.Paragraph Para(string? style, params W.Run[] runs)
    {
        var p = new W.Paragraph();
        if (style is not null)
        {
            p.Append(new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }));
        }
        p.Append(runs);
        return p;
    }

    private static W.Paragraph ListPara(int level, params W.Run[] runs)
    {
        var p = new W.Paragraph(new W.ParagraphProperties(new W.NumberingProperties(
            new W.NumberingLevelReference { Val = level }, new W.NumberingId { Val = 1 })));
        p.Append(runs);
        return p;
    }

    private static W.Run Run(string text, bool bold = false, bool italic = false)
    {
        var run = new W.Run();
        if (bold || italic)
        {
            var rp = new W.RunProperties();
            if (bold)
            {
                rp.Append(new W.Bold());
            }
            if (italic)
            {
                rp.Append(new W.Italic());
            }
            run.Append(rp);
        }
        run.Append(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static W.TableCell Cell(string text) => new(new W.Paragraph(new W.Run(new W.Text(text))));

    // ---- XLSX --------------------------------------------------------------------------------

    public sealed record SheetSpec(string Name, S.Row[] Rows);

    public static string CreateXlsx(string path, params SheetSpec[] sheets)
    {
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var wb = doc.AddWorkbookPart();
        wb.Workbook = new S.Workbook();
        var sst = wb.AddNewPart<SharedStringTablePart>();
        sst.SharedStringTable = new S.SharedStringTable(
            new S.SharedStringItem(new S.Text("Name")),
            new S.SharedStringItem(new S.Text("Amount")),
            new S.SharedStringItem(new S.Run(new S.Text("rich ")), new S.Run(new S.Text("text"))));
        var styles = wb.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new S.Stylesheet(
            new S.NumberingFormats(new S.NumberingFormat { NumberFormatId = 164, FormatCode = "dd.mm.yyyy hh:mm" }),
            new S.CellFormats(
                new S.CellFormat { NumberFormatId = 0 },
                new S.CellFormat { NumberFormatId = 14, ApplyNumberFormat = true },
                new S.CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
                new S.CellFormat { NumberFormatId = 10, ApplyNumberFormat = true }));
        var sheetList = wb.Workbook.AppendChild(new S.Sheets());
        uint id = 1;
        foreach (var spec in sheets)
        {
            var ws = wb.AddNewPart<WorksheetPart>();
            ws.Worksheet = new S.Worksheet(new S.SheetData(spec.Rows));
            sheetList.Append(new S.Sheet { Name = spec.Name, SheetId = id++, Id = wb.GetIdOfPart(ws) });
        }
        return path;
    }

    public static S.Row Row(uint index, params S.Cell[] cells) => new(cells) { RowIndex = index };

    public static S.Cell Shared(string reference, int index) =>
        new() { CellReference = reference, DataType = S.CellValues.SharedString, CellValue = new S.CellValue(index.ToString(System.Globalization.CultureInfo.InvariantCulture)) };

    public static S.Cell Inline(string reference, string text) =>
        new(new S.InlineString(new S.Text(text) { Space = SpaceProcessingModeValues.Preserve })) { CellReference = reference, DataType = S.CellValues.InlineString };

    public static S.Cell Number(string reference, string value, uint style = 0) =>
        new() { CellReference = reference, CellValue = new S.CellValue(value), StyleIndex = style == 0 ? null : style };

    public static S.Cell Bool(string reference, bool value) =>
        new() { CellReference = reference, DataType = S.CellValues.Boolean, CellValue = new S.CellValue(value ? "1" : "0") };

    // ---- PPTX --------------------------------------------------------------------------------

    public static string CreatePptx(string path, params string[][] slides)
    {
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var pres = doc.AddPresentationPart();
        pres.Presentation = new P.Presentation(new P.SlideIdList());
        uint slideId = 256;
        foreach (var paragraphs in slides)
        {
            var part = pres.AddNewPart<SlidePart>();
            var body = new P.TextBody(new A.BodyProperties(), new A.ListStyle());
            foreach (var text in paragraphs)
            {
                body.Append(new A.Paragraph(new A.Run(new A.Text(text))));
            }
            part.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(),
                new P.Shape(
                    new P.NonVisualShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 2, Name = "Text" },
                        new P.NonVisualShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.ShapeProperties(),
                    body))));
            pres.Presentation.SlideIdList!.Append(new P.SlideId { Id = slideId++, RelationshipId = pres.GetIdOfPart(part) });
        }
        return path;
    }

    /// <summary>A fake encrypted Office file: OLE signature plus the stream names of an encrypted package.</summary>
    public static string CreateFakeEncryptedOffice(string path)
    {
        var bytes = new byte[4096];
        byte[] ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        ole.CopyTo(bytes, 0);
        System.Text.Encoding.Unicode.GetBytes("EncryptionInfo").CopyTo(bytes, 1024);
        System.Text.Encoding.Unicode.GetBytes("EncryptedPackage").CopyTo(bytes, 1152);
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }
}
