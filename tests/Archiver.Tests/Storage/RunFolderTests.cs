using Archiver.Storage;
using Archiver.Tests.TestSupport;

namespace Archiver.Tests.Storage;

public class RunFolderTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 7, 0, 5, TimeSpan.FromHours(-4));

    [Fact]
    public void Create_names_folder_by_local_start_minute()
    {
        using var tmp = new TempDir();
        var folder = RunFolder.Create(tmp.Path, Start, dryRun: false);

        Assert.Equal("2026-09-29_0700", folder.RunId);
        Assert.True(Directory.Exists(Path.Combine(tmp.Path, "raw", "2026-09-29_0700")));
    }

    [Fact]
    public void Second_run_in_same_minute_gets_seconds_suffix()
    {
        using var tmp = new TempDir();
        RunFolder.Create(tmp.Path, Start, dryRun: false);
        var second = RunFolder.Create(tmp.Path, Start, dryRun: false);

        Assert.Equal("2026-09-29_070005", second.RunId);
    }

    [Fact]
    public void Dry_run_uses_fixed_folder_and_clears_it()
    {
        using var tmp = new TempDir();
        var first = RunFolder.Create(tmp.Path, Start, dryRun: true);
        File.WriteAllText(Path.Combine(first.FullPath, "stale.json"), "{}");

        var second = RunFolder.Create(tmp.Path, Start, dryRun: true);

        Assert.Equal(RunFolder.DryRunId, second.RunId);
        Assert.False(File.Exists(Path.Combine(second.FullPath, "stale.json")));
    }

    [Fact]
    public void File_paths_follow_the_spec_layout()
    {
        using var tmp = new TempDir();
        var f = RunFolder.Create(tmp.Path, Start, dryRun: false);

        Assert.Equal("raw/2026-09-29_0700/adzuna/search/net-developer-gta-p2.json", f.Relative(f.AdzunaSearchFile(".net developer", "gta", 2)));
        Assert.Equal("raw/2026-09-29_0700/adzuna/histogram/tech-lead-gta.json", f.Relative(f.AdzunaAggregateFile("histogram", "tech lead")));
        Assert.Equal("raw/2026-09-29_0700/boards/greenhouse-faire.json", f.Relative(f.BoardFile("greenhouse", "faire")));
        Assert.Equal("raw/2026-09-29_0700/jobbank/package_show.json", f.Relative(f.JobBankCatalogFile()));
        Assert.Equal("raw/2026-09-29_0700/manifest.json", f.Relative(f.ManifestFile()));
    }
}
