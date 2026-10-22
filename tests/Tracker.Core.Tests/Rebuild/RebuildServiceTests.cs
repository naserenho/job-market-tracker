using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tracker.Analysis;
using Tracker.Interpreting;
using Tracker.Rebuild;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Rebuild;

[Collection("postgres")]
[Trait("Category", "Integration")]
public class RebuildServiceTests(PostgresFixture pg)
{
    private static readonly SkillCatalog Catalog = SkillCatalog.Load(RepoConfig.PathOf("skills.json"));

    private static RebuildService Service(Tracker.Data.TrackerDb db) =>
        new(db, RepoConfig.Toronto, Catalog, NullLogger<RebuildService>.Instance);

    [Fact]
    public async Task Rebuilds_the_fixture_archive_and_loads_it_for_analysis()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        await using var db = await pg.OpenAsync();

        var summary = await Service(db).RunAsync(tmp.Path, CancellationToken.None);

        Assert.Equal((3, 8, 6), (summary.Runs, summary.Postings, summary.CountedPostings));
        Assert.Empty(summary.Warnings);
        Assert.Equal("greenhouse:101", (await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "adzuna:A3")).DuplicateOf);
        Assert.False((await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "greenhouse:102")).InScope);

        var g1 = await db.Postings.AsNoTracking().Include(p => p.Sightings).Include(p => p.Skills).ThenInclude(s => s.Skill)
            .SingleAsync(p => p.Id == "greenhouse:101");
        Assert.Equal(3, g1.Sightings.Count);
        Assert.Equal(("parsed", 130000m), (g1.SalaryOrigin, g1.SalaryMin!.Value));
        Assert.Superset(new HashSet<string> { "React", "TypeScript", "C#" }, g1.Skills.Select(s => s.Skill!.Name).ToHashSet());

        var s1 = await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "ashby:S1");
        Assert.True(s1.IsRemoteInCountry);
        Assert.True(s1.CitizenshipOrClearance);

        var (postings, runs, jobBank) = await AnalysisLoader.LoadAsync(db, RepoConfig.Toronto, CancellationToken.None);
        Assert.Equal(6, postings.Count);
        Assert.Equal(3, runs.Count);
        Assert.Empty(jobBank);
        var inbox = postings.Single(p => p.Id == "inbox:one.txt");
        Assert.Equal("Toronto", inbox.Location);
        Assert.Equal(runs[^1].StartedAt, Assert.Single(inbox.Sightings));
        Assert.Equal("Remote (Canada)", postings.Single(p => p.Id == "ashby:S1").Location);
        var remote = postings.Single(p => p.Id == "ashby:S1");
        Assert.Equal((false, true), (remote.IsLocal, remote.IsRemoteInCountry));
        var toronto = postings.Single(p => p.Id == "adzuna:A1");
        Assert.Equal((true, "Toronto"), (toronto.IsLocal, toronto.City));
        Assert.Equal(RoleFamily.Dotnet, postings.Single(p => p.Id == "adzuna:A1").Family);
    }

    [Fact]
    public async Task Rebuilding_the_same_archive_twice_is_idempotent()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        await using var db = await pg.OpenAsync();

        async Task<(int, int, int)> Counts() =>
            (await db.Postings.CountAsync(), await db.PostingSightings.CountAsync(), await db.PostingSkills.CountAsync());

        await Service(db).RunAsync(tmp.Path, CancellationToken.None);
        var first = await Counts();
        await Service(db).RunAsync(tmp.Path, CancellationToken.None);

        Assert.Equal(first, await Counts());
    }

    [Fact]
    public async Task A_corrupt_raw_file_becomes_a_warning_not_a_failed_rebuild()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        tmp.File($"raw/{FixtureArchive.Run2}/boards/greenhouse-broken.json", "<html>Service Unavailable</html>");
        await using var db = await pg.OpenAsync();

        var summary = await Service(db).RunAsync(tmp.Path, CancellationToken.None);

        Assert.Equal(8, summary.Postings);
        Assert.Contains(summary.Warnings, w => w.Contains("greenhouse-broken.json"));
    }

    [Fact]
    public async Task An_empty_archive_is_reported_clearly()
    {
        using var tmp = new TempDir();
        await using var db = await pg.OpenAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).RunAsync(tmp.Path, CancellationToken.None));

        Assert.Contains("Run the archiver first", error.Message);
    }

    [Fact]
    public async Task A_pasted_job_saved_before_an_earlier_run_is_sighted_only_in_that_run()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        tmp.File("inbox/one.txt", "URL: https://www.linkedin.com/jobs/view/1\nCompany: Bank of Example\nTitle: Senior .NET Developer\nLocation: Toronto, ON\nSaved: 2026-09-10\n\nWe need C#, SQL Server and Azure DevOps.\n");
        await using var db = await pg.OpenAsync();

        await Service(db).RunAsync(tmp.Path, CancellationToken.None);

        var runs = await db.PostingSightings.Where(s => s.PostingId == "inbox:one.txt").Select(s => s.RunId).ToListAsync();
        Assert.Equal(new[] { FixtureArchive.Run2 }, runs);
    }

    // An archive holding Toronto and Abu Dhabi collections, and pasted jobs from both: each rebuild reads only its region.
    [Fact]
    public async Task A_rebuild_reads_only_the_active_regions_runs_and_pasted_files()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path); // three Toronto runs, with no region in their manifests
        tmp.File("raw/2026-10-02_0900/manifest.json", """{"runId":"2026-10-02_0900","startedAt":"2026-10-02T09:00:00+04:00","region":"abu-dhabi"}""");
        tmp.File("raw/2026-10-02_0900/boards/greenhouse-acme.json",
            """{"jobs":[{"id":901,"title":"Senior Software Engineer","location":{"name":"Abu Dhabi, United Arab Emirates"},"absolute_url":"https://x.test/901","content":"C# and Azure."}]}""");
        tmp.File("inbox/uae.txt", "Company: Bayt Co\nTitle: Developer\nLocation: Abu Dhabi\nSaved: 2026-10-02\nRegion: abu-dhabi\n\nC# and Azure.\n");
        await using var db = await pg.OpenAsync();

        var toronto = await Service(db).RunAsync(tmp.Path, CancellationToken.None);

        Assert.Equal(3, toronto.Runs);
        Assert.False(await db.Postings.AnyAsync(p => p.Id == "greenhouse:901" || p.Id == "inbox:uae.txt"));
        Assert.Contains(toronto.Warnings, w => w.Contains("1 collection") && w.Contains("abu-dhabi"));
        Assert.Contains(toronto.Warnings, w => w.Contains("1 inbox file") && w.Contains("another region"));

        var abuDhabi = await new RebuildService(db, RepoConfig.AbuDhabi, Catalog, NullLogger<RebuildService>.Instance)
            .RunAsync(tmp.Path, CancellationToken.None);

        Assert.Equal(1, abuDhabi.Runs);
        var posting = await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "greenhouse:901");
        Assert.Equal(("Abu Dhabi", true), (posting.City, posting.IsLocal));
        Assert.True(await db.Postings.AnyAsync(p => p.Id == "inbox:uae.txt"));
        // A pasted file without a Region line belongs to the active region (spec); this Toronto one is simply out of scope here.
        Assert.False((await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "inbox:one.txt")).InScope);
    }

    [Fact]
    public async Task A_region_with_no_runs_says_so()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        await using var db = await pg.OpenAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RebuildService(db, RepoConfig.AbuDhabi, Catalog, NullLogger<RebuildService>.Instance).RunAsync(tmp.Path, CancellationToken.None));

        Assert.Contains("abu-dhabi", error.Message);
    }
}
