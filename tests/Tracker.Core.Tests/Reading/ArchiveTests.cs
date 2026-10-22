using Tracker.Reading;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Reading;

public class ArchiveTests
{
    [Fact]
    public void Runs_are_completed_real_runs_oldest_first()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);

        var runs = Archive.Runs(tmp.Path);

        Assert.Equal(new[] { FixtureArchive.Run1, FixtureArchive.Run2, FixtureArchive.Run3 }, runs.Select(r => r.RunId));
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 7, 0, 0, TimeSpan.FromHours(-4)), runs[0].StartedAt);
    }

    [Fact]
    public void ReadPostings_reads_every_source_in_a_run()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        var run2 = Archive.Runs(tmp.Path)[1];
        var warnings = new List<string>();

        var keys = Archive.ReadPostings(run2, RepoConfig.Toronto, warnings).Select(p => p.Key).Order().ToList();

        Assert.Equal(new[] { "adzuna:A1", "greenhouse:101", "lever:L1" }, keys);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ReadPostings_reads_workable_boards_with_a_dashed_slug()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        tmp.File($"raw/{FixtureArchive.Run2}/boards/workable-49-solutions-1.json",
            """{"name":"49 Solutions","jobs":[{"title":"Developer","shortcode":"AB12","url":"https://apply.workable.com/j/AB12","locations":[]}]}""");
        var warnings = new List<string>();

        var keys = Archive.ReadPostings(Archive.Runs(tmp.Path)[1], RepoConfig.Ontario, warnings).Select(p => p.Key).ToList();

        Assert.Contains("workable:AB12", keys);
        Assert.Empty(warnings);
    }

    // Details sit in a folder named like the list file; they are read with it, never as boards of their own.
    [Fact]
    public void ReadPostings_reads_smartrecruiters_lists_with_their_detail_files()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        tmp.File($"raw/{FixtureArchive.Run2}/boards/smartrecruiters-gdmsi.json",
            """{"offset":0,"limit":100,"totalFound":1,"content":[{"id":"77","name":"Developer","location":{"city":"Ottawa","country":"ca","remote":false}}]}""");
        tmp.File($"raw/{FixtureArchive.Run2}/boards/smartrecruiters-gdmsi/77.json",
            """{"id":"77","jobAd":{"sections":{"jobDescription":{"text":"<p>C# and Azure.</p>"}}}}""");
        var warnings = new List<string>();

        var posting = Assert.Single(Archive.ReadPostings(Archive.Runs(tmp.Path)[1], RepoConfig.Ontario, warnings), p => p.Source == "smartrecruiters");

        Assert.Equal("C# and Azure.", posting.Description);
        Assert.Empty(warnings);
    }

    [Fact]
    public void An_unreadable_file_is_skipped_with_a_warning_and_the_rest_is_read()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        tmp.File($"raw/{FixtureArchive.Run2}/boards/greenhouse-broken.json", "<html>Service Unavailable</html>");
        tmp.File($"raw/{FixtureArchive.Run2}/adzuna/search/zz-broken-gta-p1.json", "");
        var run2 = Archive.Runs(tmp.Path)[1];
        var warnings = new List<string>();

        var keys = Archive.ReadPostings(run2, RepoConfig.Toronto, warnings).Select(p => p.Key).Order().ToList();

        Assert.Equal(new[] { "adzuna:A1", "greenhouse:101", "lever:L1" }, keys);
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("greenhouse-broken.json"));
        Assert.Contains(warnings, w => w.Contains("zz-broken-gta-p1.json"));
    }

    [Fact]
    public void ReadAggregates_keeps_payload_and_term()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        var run3 = Archive.Runs(tmp.Path)[2];

        var agg = Assert.Single(Archive.ReadAggregates(run3, new List<string>()));

        Assert.Equal(new AdzunaAggregate(FixtureArchive.Run3, "net-developer", "histogram", """{"histogram":{"100000":7}}"""), agg);
    }

    [Fact]
    public void CollectionProblems_names_sources_that_did_not_fully_succeed()
    {
        using var tmp = new TempDir();
        tmp.File("raw/2026-10-02_0700/manifest.json", """
            {"runId":"2026-10-02_0700","startedAt":"2026-10-02T07:00:00-04:00","cancelled":false,
             "sources":{"adzuna":{"outcome":"succeeded","ok":48,"failed":0,"note":null},
                        "boards":{"outcome":"partial","ok":37,"failed":2,"note":null},
                        "jobbank":{"outcome":"skipped","ok":0,"failed":0,"note":"offline"}}}
            """);
        var run = Assert.Single(Archive.Runs(tmp.Path));

        var problems = Archive.CollectionProblems(run);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("boards") && p.Contains("partial") && p.Contains("2 of 39"));
        Assert.Contains(problems, p => p.Contains("jobbank") && p.Contains("skipped") && p.Contains("offline"));
    }

    [Fact]
    public void CollectionProblems_of_a_manifest_without_sources_is_empty()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        Assert.Empty(Archive.CollectionProblems(Archive.Runs(tmp.Path)[0]));
    }

    [Fact]
    public void Runs_of_an_empty_data_folder_is_empty()
    {
        using var tmp = new TempDir();
        Assert.Empty(Archive.Runs(tmp.Path));
    }

    [Fact]
    public void Runs_carry_their_region_and_old_manifests_are_toronto()
    {
        using var tmp = new TempDir();
        tmp.File("raw/2026-09-01_0700/manifest.json", """{"runId":"2026-09-01_0700","startedAt":"2026-09-01T07:00:00-04:00"}""");
        tmp.File("raw/2026-09-02_0700/manifest.json", """{"runId":"2026-09-02_0700","startedAt":"2026-09-02T07:00:00+04:00","region":"abu-dhabi"}""");

        Assert.Equal(new[] { "toronto", "abu-dhabi" }, Archive.Runs(tmp.Path).Select(r => r.Region));
    }

    // Abu Dhabi has no Adzuna or Job Bank by design: that is not a problem to warn about on every report.
    [Fact]
    public void Sources_that_do_not_apply_to_the_region_are_not_problems()
    {
        using var tmp = new TempDir();
        tmp.File("raw/2026-10-02_0900/manifest.json", """
            {"runId":"2026-10-02_0900","startedAt":"2026-10-02T09:00:00+04:00","region":"abu-dhabi",
             "sources":{"adzuna":{"outcome":"notApplicable","ok":0,"failed":0,"note":"Adzuna has no market for this region."},
                        "boards":{"outcome":"succeeded","ok":15,"failed":0,"note":null},
                        "jobbank":{"outcome":"notApplicable","ok":0,"failed":0,"note":"Job Bank is not configured for this region."}}}
            """);

        Assert.Empty(Archive.CollectionProblems(Archive.Runs(tmp.Path)[0]));
    }
}
