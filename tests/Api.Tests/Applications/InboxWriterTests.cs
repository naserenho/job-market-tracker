using Api.Applications;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

public class InboxWriterTests
{
    private static readonly DateOnly Saved = new(2027, 3, 1);

    [Fact]
    public void Content_is_a_valid_inbox_file()
    {
        var content = InboxWriter.Content("Senior .NET Developer", "Bank of Testing", "https://x.test/1", "Toronto, ON",
            "Title: not a header here\n\nWe need C# and Azure.", Saved, "toronto");

        var (posting, warning) = InboxReader.Parse("2027-03-01-bank-of-testing-senior-net-developer.txt", content, "run");

        Assert.Null(warning);
        Assert.Equal(("Senior .NET Developer", "Bank of Testing", "Toronto, ON", "https://x.test/1"),
            (posting!.Title, posting.Company, posting.Location, posting.Url));
        Assert.Contains("We need C# and Azure.", posting.Description);
        Assert.True(posting.IsFullText);
    }

    [Fact]
    public void File_names_are_dated_slugs_with_a_suffix_when_taken()
    {
        var taken = new HashSet<string> { "2027-03-01-rbc-senior-net-developer.txt", "2027-03-01-rbc-senior-net-developer-2.txt" };
        Assert.Equal("2027-03-01-rbc-senior-net-developer-3.txt", InboxWriter.FileName("RBC", "Senior .NET Developer", Saved, taken.Contains));
        Assert.Equal("2027-03-01-x-x.txt", InboxWriter.FileName("???", "!!!", Saved, _ => false));
    }

    [Fact]
    public void Write_saves_the_file_and_returns_its_future_posting_id()
    {
        using var tmp = new TempDir();
        var inbox = Path.Combine(tmp.Path, "inbox");

        var (postingId, path) = InboxWriter.Write(inbox, "Tech Lead", "Shopify", "", "Remote (Canada)", "Lead a team of five.", Saved, "toronto");

        Assert.Equal("inbox:2027-03-01-shopify-tech-lead.txt", postingId);
        Assert.Equal(Path.Combine(inbox, "2027-03-01-shopify-tech-lead.txt"), path);
        Assert.StartsWith("URL: \nCompany: Shopify\nTitle: Tech Lead\n", File.ReadAllText(path));
    }

    [Fact]
    public void Write_refuses_text_the_rebuild_could_not_read()
    {
        using var tmp = new TempDir();
        var error = Assert.Throws<TrackerInputException>(() => InboxWriter.Write(Path.Combine(tmp.Path, "inbox"), "Dev", "Acme", "", "", "   ", Saved, "toronto"));
        Assert.Equal("postingText", error.Field);
        Assert.False(Directory.Exists(Path.Combine(tmp.Path, "inbox")) && Directory.EnumerateFiles(Path.Combine(tmp.Path, "inbox")).Any());
    }

    // The rebuild of another region skips the file instead of counting a Toronto job in Abu Dhabi.
    [Fact]
    public void Content_records_the_region()
    {
        var content = InboxWriter.Content("Dev", "Co", "", "Abu Dhabi", "C#", Saved, "abu-dhabi");

        Assert.Contains("\nSaved: 2027-03-01\nRegion: abu-dhabi\n\n", content);
        Assert.Equal("abu-dhabi", InboxReader.RegionOf(content));
    }
}
