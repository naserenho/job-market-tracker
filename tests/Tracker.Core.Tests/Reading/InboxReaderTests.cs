using Tracker.Reading;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Reading;

public class InboxReaderTests
{
    private static readonly IReadOnlyList<ArchiveRun> Runs =
    [
        new("r1", new DateTimeOffset(2026, 9, 1, 7, 0, 0, TimeSpan.FromHours(-4)), ""),
        new("r2", new DateTimeOffset(2026, 9, 15, 7, 0, 0, TimeSpan.FromHours(-4)), ""),
        new("r3", new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.FromHours(-4)), ""),
    ];

    private static string Pasted(string saved) => $"Title: Dev\nCompany: A\nLocation: Toronto\n{saved}\n\nBody.";

    [Fact]
    public void Parse_reads_headers_then_body()
    {
        var (p, warning) = InboxReader.Parse("a.txt", "URL: https://x.test/1\r\nCompany: Bank\r\nTitle: Senior .NET Developer\r\nLocation: Toronto, ON\r\nSaved: 2026-09-01\r\n\r\nWe need C#.\r\nAnd SQL.", "r9");

        Assert.Null(warning);
        Assert.NotNull(p);
        Assert.Equal("inbox:a.txt", p.Key);
        Assert.Equal("https://x.test/1", p.Url);
        Assert.Equal("Bank", p.Company);
        Assert.Equal("Toronto, ON", p.Location);
        Assert.Equal("We need C#.\nAnd SQL.", p.Description);
        Assert.Equal("r9", p.RunId);
        Assert.True(p.IsFullText);
    }

    [Fact]
    public void Missing_optional_headers_warn_but_keep_the_posting()
    {
        var (p, warning) = InboxReader.Parse("b.txt", "Title: Developer\n\nBody text.", "r1");
        Assert.NotNull(p);
        Assert.Contains("Company", warning);
        Assert.Contains("Location", warning);
    }

    [Fact]
    public void Missing_title_or_body_skips_the_file_with_a_warning()
    {
        Assert.Null(InboxReader.Parse("c.txt", "Just pasted text without headers", "r1").Posting);
        Assert.Contains("c.txt", InboxReader.Parse("c.txt", "Title: Dev\n\n", "r1").Warning);
    }

    [Fact]
    public void ReadAll_reads_only_txt_files_and_tolerates_a_missing_folder()
    {
        using var tmp = new TempDir();
        tmp.File("inbox/one.txt", "Title: Dev\nCompany: A\nLocation: Toronto\n\nBody.");
        tmp.File("inbox/README.md", "not a posting");

        Assert.Single(InboxReader.ReadAll(Path.Combine(tmp.Path, "inbox"), Runs, "toronto").Postings);
        Assert.Empty(InboxReader.ReadAll(Path.Combine(tmp.Path, "missing"), Runs, "toronto").Postings);
    }

    [Theory]
    [InlineData("Saved: 2026-08-20", "r1")] // before the first run
    [InlineData("Saved: 2026-09-01", "r1")] // the day of a run
    [InlineData("Saved: 2026-09-02", "r2")] // between runs: the next one
    [InlineData("Saved: 2026-12-25", "r3")] // later than every run: the latest
    public void A_pasted_job_is_seen_once_in_the_first_run_on_or_after_its_saved_date(string saved, string run)
    {
        var (posting, warnings) = InboxReader.Parse("a.txt", Pasted(saved), Runs);

        Assert.Equal(run, posting!.RunId);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("Saved: yesterday")]
    [InlineData("Saved: 2026-13-45")]
    [InlineData("Note: no saved line")]
    public void Without_a_readable_saved_date_it_stays_in_the_latest_run_with_a_warning(string line)
    {
        var (posting, warnings) = InboxReader.Parse("a.txt", Pasted(line), Runs);

        Assert.Equal("r3", posting!.RunId);
        Assert.Equal("Inbox file a.txt has no 'Saved: yyyy-MM-dd' line, so it never ages out of the market data.", Assert.Single(warnings));
    }

    [Fact]
    public void With_no_runs_yet_the_posting_still_parses() =>
        Assert.Equal("", InboxReader.Parse("a.txt", Pasted("Saved: 2026-09-02"), []).Posting!.RunId);

    [Theory]
    [InlineData("Title: Dev\nRegion: abu-dhabi\n\nBody.", "abu-dhabi")]
    [InlineData("Title: Dev\nregion:  Toronto \n\nBody.", "Toronto")]
    [InlineData("Title: Dev\n\nRegion: abu-dhabi is in the body, not a header.", null)]
    public void RegionOf_reads_the_header_only(string content, string? region) =>
        Assert.Equal(region, InboxReader.RegionOf(content));

    [Fact]
    public void ReadAll_skips_files_from_another_region_with_one_warning()
    {
        using var tmp = new TempDir();
        tmp.File("inbox/a.txt", "Title: Dev\nCompany: A\nLocation: Toronto\nSaved: 2026-09-01\n\nBody.");
        tmp.File("inbox/b.txt", "Title: Dev\nCompany: B\nLocation: Abu Dhabi\nSaved: 2026-09-01\nRegion: abu-dhabi\n\nBody.");
        tmp.File("inbox/c.txt", "Title: Dev\nCompany: C\nLocation: Toronto\nSaved: 2026-09-01\nRegion: TORONTO\n\nBody.");

        var result = InboxReader.ReadAll(Path.Combine(tmp.Path, "inbox"), Runs, "toronto");

        Assert.Equal(new[] { "a.txt", "c.txt" }, result.Postings.Select(p => p.SourceId));
        Assert.Equal("1 inbox file belongs to another region and was skipped.", Assert.Single(result.Warnings));
    }
}
