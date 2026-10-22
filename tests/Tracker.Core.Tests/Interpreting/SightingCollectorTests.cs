using Tracker.Interpreting;
using Tracker.Reading;

namespace Tracker.Core.Tests.Interpreting;

public class SightingCollectorTests
{
    private static RawPosting Raw(string id, string run, string title) =>
        new("adzuna", id, run, "Co", title, "", "Toronto", null, "CA", "", false, null);

    [Fact]
    public void Latest_content_wins_and_each_run_counts_once()
    {
        var c = new SightingCollector();
        c.Add(Raw("1", "r1", "Old title"));
        c.Add(Raw("1", "r1", "Old title")); // same job found by two search terms in one run
        c.Add(Raw("1", "r2", "New title"));
        c.Add(Raw("2", "r2", "Other"));

        var entries = c.Entries.OrderBy(e => e.Latest.SourceId).ToList();

        Assert.Equal("New title", entries[0].Latest.Title);
        Assert.Equal(new[] { "r1", "r2" }, entries[0].Runs);
        Assert.Equal(new[] { "r2" }, entries[1].Runs);
    }
}
