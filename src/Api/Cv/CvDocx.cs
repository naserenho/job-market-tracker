using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Api.Cv;

// Writes CvLayout's paragraphs as a Word document built for applicant-tracking systems (ADR 0013): one column, US Letter,
// Word's own Title and Heading styles, real bullets, and nothing in tables, text boxes, headers or footers.
public static partial class CvDocx
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const int MaxFileName = 120;
    private const int BulletList = 1;

    public static byte[] Render(IReadOnlyList<CvParagraph> paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.AddNewPart<StyleDefinitionsPart>().Styles = Styles();
            main.AddNewPart<NumberingDefinitionsPart>().Numbering = Numbering();
            var body = new Body();
            foreach (var paragraph in paragraphs) body.Append(Paragraph(main, paragraph));
            body.Append(new SectionProperties(
                new PageSize { Width = 12240U, Height = 15840U }, // US Letter, in twentieths of a point
                new PageMargin { Top = 1008, Right = 1008U, Bottom = 1008, Left = 1008U, Header = 720U, Footer = 720U, Gutter = 0U })); // 0.7 inch
            main.Document = new Document(body);
        }
        return stream.ToArray();
    }

    // "<name> - CV - <company>.docx" without characters Windows forbids, at most 120 characters.
    public static string FileName(string personName, string? company)
    {
        var stem = string.IsNullOrWhiteSpace(company) ? $"{personName} - CV" : $"{personName} - CV - {company}";
        var cleaned = Spaces().Replace(Invalid().Replace(stem, ""), " ").Trim();
        if (cleaned.Length == 0) cleaned = "CV";
        if (cleaned.Length > MaxFileName - 5)
        {
            cleaned = cleaned[..(MaxFileName - 5)];
            if (char.IsHighSurrogate(cleaned[^1])) cleaned = cleaned[..^1];
            cleaned = cleaned.TrimEnd();
        }
        return cleaned + ".docx";
    }

    private static Paragraph Paragraph(MainDocumentPart main, CvParagraph source)
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = StyleId(source.Style) });
        if (source.Style == CvStyle.Bullet)
            properties.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = BulletList }));
        var paragraph = new Paragraph(properties);
        foreach (var run in source.Runs)
        {
            if (run.Url is { } url)
            {
                var link = main.AddHyperlinkRelationship(new Uri(url, UriKind.Absolute), true);
                paragraph.Append(new Hyperlink(RunOf(run.Text, new RunProperties(new RunStyle { Val = "Hyperlink" }))) { Id = link.Id });
            }
            else
            {
                paragraph.Append(RunOf(run.Text, run.Bold ? new RunProperties(new Bold()) : null));
            }
        }
        return paragraph;
    }

    private static Run RunOf(string text, RunProperties? properties = null)
    {
        var run = new Run();
        if (properties is not null) run.Append(properties);
        run.Append(Content(text));
        return run;
    }

    // A run's content: its text, with a Word line break wherever the source has a line break (a summary typed on
    // several lines).
    private static IEnumerable<OpenXmlElement> Content(string value)
    {
        var lines = Clean(value).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) yield return new Break();
            if (lines[i].Length > 0) yield return new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve };
        }
    }

    // Characters XML can't hold are dropped: control characters from a pasted summary, and half of a surrogate pair.
    // Whole pairs (emoji) are kept.
    private static string Clean(string value)
    {
        var kept = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) kept.Append(c).Append(value[++i]);
            else if (!char.IsSurrogate(c) && XmlConvert.IsXmlChar(c)) kept.Append(c);
        }
        return kept.ToString();
    }

    private static string StyleId(CvStyle style) => style switch
    {
        CvStyle.Name => "Title",
        CvStyle.Headline => "Subtitle",
        CvStyle.Heading => "Heading1",
        CvStyle.EntryTitle => "Heading2",
        CvStyle.EntryMeta => "EntryMeta",
        CvStyle.Bullet => "ListBullet",
        _ => "Normal",
    };

    private static Styles Styles() => new(
        new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", EastAsia = "Calibri", ComplexScript = "Calibri" },
                new FontSize { Val = "21" }, new FontSizeComplexScript { Val = "21" }, new Languages { Val = "en-CA" })),
            new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                new SpacingBetweenLines { After = "40", Line = "252", LineRule = LineSpacingRuleValues.Auto }))),
        ParagraphStyle("Normal", "Normal", basedOn: null, size: null, bold: false),
        ParagraphStyle("Title", "Title", "Normal", size: 40, bold: true),
        ParagraphStyle("Subtitle", "Subtitle", "Normal", size: 24, bold: true, before: 60, after: 60),
        ParagraphStyle("Heading1", "heading 1", "Normal", size: 24, bold: true, before: 200, after: 60, outline: 0),
        ParagraphStyle("Heading2", "heading 2", "Normal", size: 22, bold: true, before: 120, after: 0, outline: 1),
        ParagraphStyle("EntryMeta", "Entry Meta", "Normal", size: null, bold: false, italic: true),
        ParagraphStyle("ListBullet", "List Bullet", "Normal", size: null, bold: false, after: 20),
        new Style(new StyleName { Val = "Hyperlink" }, new UIPriority { Val = 99 }, new UnhideWhenUsed(),
            new StyleRunProperties(new Color { Val = "0563C1" }, new Underline { Val = UnderlineValues.Single }))
        { Type = StyleValues.Character, StyleId = "Hyperlink" });

    // Children in schema order (name, basedOn, next, qFormat, pPr, rPr): the validator test fails on any other order.
    private static Style ParagraphStyle(string id, string name, string? basedOn, int? size, bool bold, bool italic = false,
        int before = 0, int after = 40, int? outline = null)
    {
        var style = new Style { Type = StyleValues.Paragraph, StyleId = id };
        if (id == "Normal") style.Default = true;
        style.Append(new StyleName { Val = name });
        if (basedOn is not null) style.Append(new BasedOn { Val = basedOn }, new NextParagraphStyle { Val = "Normal" });
        style.Append(new PrimaryStyle());
        var paragraph = new StyleParagraphProperties();
        if (outline is not null) paragraph.Append(new KeepNext());
        paragraph.Append(new SpacingBetweenLines { Before = before.ToString(), After = after.ToString() });
        if (outline is { } level) paragraph.Append(new OutlineLevel { Val = level });
        style.Append(paragraph);
        var run = new StyleRunProperties();
        if (bold) run.Append(new Bold());
        if (italic) run.Append(new Italic());
        if (size is { } halfPoints) run.Append(new FontSize { Val = halfPoints.ToString() }, new FontSizeComplexScript { Val = halfPoints.ToString() });
        if (run.HasChildren) style.Append(run);
        return style;
    }

    private static Numbering Numbering() => new(
        new AbstractNum(
            new MultiLevelType { Val = MultiLevelValues.SingleLevel },
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "•" },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = "360", Hanging = "240" }))
            { LevelIndex = 0 })
        { AbstractNumberId = BulletList },
        new NumberingInstance(new AbstractNumId { Val = BulletList }) { NumberID = BulletList });

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1F]")]
    private static partial Regex Invalid();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
