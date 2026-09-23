using System.Text;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>Reads slide texts in presentation order. One slide part is loaded at a time.</summary>
internal sealed class PptxReader
{
    private readonly PresentationPart _presentation;

    public PptxReader(PresentationDocument document)
    {
        _presentation = document.PresentationPart ?? throw new InvalidDataException("presentation part missing");
    }

    /// <summary>Slide parts in the order of the slide list (not the order in the package).</summary>
    public IReadOnlyList<SlidePart> Slides()
    {
        var result = new List<SlidePart>();
        var ids = _presentation.Presentation?.SlideIdList?.Elements<P.SlideId>() ?? [];
        foreach (var id in ids)
        {
            if (id.RelationshipId?.Value is { } rel && _presentation.TryGetPartById(rel, out var part) && part is SlidePart slide)
            {
                result.Add(slide);
            }
        }
        return result;
    }

    /// <summary>Non-empty text paragraphs of one slide, in shape tree order (tables included).</summary>
    public static IReadOnlyList<string> SlideParagraphs(SlidePart slide)
    {
        var tree = slide.Slide?.CommonSlideData?.ShapeTree;
        if (tree is null)
        {
            return [];
        }
        var result = new List<string>();
        foreach (var paragraph in tree.Descendants<A.Paragraph>())
        {
            var sb = new StringBuilder();
            foreach (var child in paragraph.ChildElements)
            {
                switch (child)
                {
                    case A.Run run:
                        sb.Append(run.Text?.Text);
                        break;
                    case A.Field field:
                        sb.Append(field.Text?.Text);
                        break;
                    case A.Break:
                        sb.Append('\n');
                        break;
                }
            }
            var text = sb.ToString().Trim();
            if (text.Length > 0)
            {
                result.Add(text);
            }
        }
        return result;
    }
}
