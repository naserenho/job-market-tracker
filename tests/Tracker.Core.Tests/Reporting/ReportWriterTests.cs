using Tracker.Reporting;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Reporting;

public class ReportWriterTests
{
    [Fact]
    public async Task Writes_run_latest_and_fragment_files_without_leftovers()
    {
        using var tmp = new TempDir();

        var latest = await ReportWriter.WriteAsync(tmp.Path, ReportRendererTests.Model(), RepoConfig.Toronto, CancellationToken.None);

        var dir = Path.Combine(tmp.Path, "reports");
        Assert.Equal(Path.Combine(dir, "latest.html"), latest);
        Assert.Equal(File.ReadAllText(latest), File.ReadAllText(Path.Combine(dir, "2026-10-02_0700.html")));
        Assert.StartsWith("<title>", File.ReadAllText(Path.Combine(dir, "latest.fragment.html")));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
}
