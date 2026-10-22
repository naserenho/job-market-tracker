using Api.Cv;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using static Api.Tests.Cv.CvSamples;

namespace Api.Tests.Cv;

public class CvDocxTests
{
    private static IReadOnlyList<CvParagraph> Layout(MasterCv? cv = null, CvChoices? choices = null) =>
        CvLayout.Paragraphs(cv ?? SampleCv(), choices ?? new CvChoices("Senior .NET Developer", "Builds .NET systems.", ["a2", "a1", "g1"]), Set("Docker"));

    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), false);

    private static IEnumerable<string> Errors(WordprocessingDocument doc) =>
        new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).Select(e => $"{e.Path?.XPath}: {e.Description}");

    [Fact]
    public void The_document_is_valid_for_word()
    {
        using var doc = Open(CvDocx.Render(Layout()));
        Assert.Empty(Errors(doc));
    }

    [Fact]
    public void Body_text_reads_in_page_order()
    {
        var layout = Layout();
        using var doc = Open(CvDocx.Render(layout));

        Assert.Equal(layout.Select(p => p.Text), doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Select(p => p.InnerText));
    }

    [Fact]
    public void Headings_and_bullets_use_word_styles_on_letter_paper()
    {
        using var doc = Open(CvDocx.Render(Layout()));
        var body = doc.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Elements<Paragraph>().ToList();
        string? StyleOf(string text) => paragraphs.First(p => p.InnerText == text).ParagraphProperties?.ParagraphStyleId?.Val?.Value;

        Assert.Equal("Title", StyleOf("Alex Example"));
        Assert.Equal("Heading1", StyleOf("Experience"));
        Assert.Equal("Heading2", StyleOf("Senior Developer — Acme Corp"));
        Assert.Equal("ListBullet", StyleOf("Built CI/CD."));
        Assert.NotNull(paragraphs.First(p => p.InnerText == "Built CI/CD.").ParagraphProperties!.NumberingProperties);
        Assert.Equal(12240U, body.Elements<SectionProperties>().Single().GetFirstChild<PageSize>()!.Width!.Value);
    }

    [Fact]
    public void Nothing_hides_in_headers_tables_or_drawings()
    {
        using var doc = Open(CvDocx.Render(Layout()));
        var main = doc.MainDocumentPart!;

        Assert.Empty(main.HeaderParts);
        Assert.Empty(main.FooterParts);
        Assert.Empty(main.Document!.Descendants<Table>());
        Assert.Empty(main.Document!.Descendants<Drawing>());
    }

    [Fact]
    public void Links_are_real_hyperlinks()
    {
        using var doc = Open(CvDocx.Render(Layout()));
        var main = doc.MainDocumentPart!;

        var link = Assert.Single(main.Document!.Descendants<Hyperlink>());
        Assert.Equal("https://github.com/alex-example", link.InnerText);
        Assert.Equal(new Uri("https://github.com/alex-example"), main.HyperlinkRelationships.Single(r => r.Id == link.Id).Uri);
    }

    [Fact]
    public void Special_characters_survive_and_stay_valid()
    {
        var cv = SampleCv(sections: [Role("r", "R&D <Lead>", "\"Quotes\" & Co", 3, B("x", "Shipped 🚀 features for R&D <fast>\u000B."))]);

        using var doc = Open(CvDocx.Render(Layout(cv, new CvChoices("Dev & <Lead>", "", ["x"]))));

        Assert.Empty(Errors(doc));
        var texts = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Select(p => p.InnerText).ToList();
        Assert.Contains("Shipped 🚀 features for R&D <fast>.", texts);
        Assert.Contains("R&D <Lead> — \"Quotes\" & Co", texts);
    }

    [Theory]
    [InlineData("Alex Example", "Acme Corp", "Alex Example - CV - Acme Corp.docx")]
    [InlineData("Alex Example", null, "Alex Example - CV.docx")]
    [InlineData("Alex Example", "AT&T / Bell: Canada?", "Alex Example - CV - AT&T Bell Canada.docx")]
    [InlineData("Alex  Example", "..\\..\\Windows", "Alex Example - CV - ....Windows.docx")]
    public void File_names_are_safe_for_windows(string name, string? company, string expected) =>
        Assert.Equal(expected, CvDocx.FileName(name, company));

    [Fact]
    public void Long_file_names_are_cut_to_120_characters()
    {
        var name = CvDocx.FileName("Alex Example", new string('x', 300));

        Assert.Equal(120, name.Length);
        Assert.EndsWith(".docx", name);
    }

    private static WordprocessingDocument Render(string text) => Open(CvDocx.Render([CvParagraph.Plain(CvStyle.Paragraph, text)]));

    [Fact]
    public void Line_breaks_in_a_paragraph_become_word_line_breaks()
    {
        using var doc = Render("First line.\r\nSecond line.\nThird line.");
        var paragraph = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Single();

        Assert.Equal(new[] { "First line.", "Second line.", "Third line." }, paragraph.Descendants<Text>().Select(t => t.Text));
        Assert.Equal(2, paragraph.Descendants<Break>().Count());
        Assert.Empty(Errors(doc));
    }

    [Theory]
    [InlineData("\n\n")]
    [InlineData("Ends with a break.\n")]
    public void Stray_line_breaks_never_make_an_invalid_document(string text)
    {
        using var doc = Render(text);

        Assert.Empty(Errors(doc));
        Assert.DoesNotContain(doc.MainDocumentPart!.Document!.Body!.Descendants<Text>(), t => t.Text.Contains('\n'));
    }

    [Fact]
    public void Half_an_emoji_is_dropped_and_whole_ones_are_kept()
    {
        using var doc = Render("Half \uD83D here, low \uDE00 there, whole \U0001F600.");

        Assert.Equal("Half  here, low  there, whole \U0001F600.", doc.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.Empty(Errors(doc));
    }
}
