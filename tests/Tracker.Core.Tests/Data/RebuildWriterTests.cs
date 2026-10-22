using Microsoft.EntityFrameworkCore;
using Tracker.Data;
using Tracker.Interpreting;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Data;

[Collection("postgres")]
[Trait("Category", "Integration")]
public class RebuildWriterTests(PostgresFixture pg)
{
    private static readonly ArchiveRun R1 = new("r1", new DateTimeOffset(2026, 9, 1, 7, 0, 0, TimeSpan.FromHours(-4)), "");
    private static readonly ArchiveRun R2 = new("r2", new DateTimeOffset(2026, 9, 4, 7, 0, 0, TimeSpan.FromHours(-4)), "");
    private static readonly SkillDefinition[] Skills = [new("C#", "language", ["C#"]), new("Azure", "cloud", ["Azure"])];

    private static RebuildData Data(params string[] skillsOfSecond) => new(
        [R1, R2],
        Skills,
        [
            Make.Posting("greenhouse:1", ["r1", "r2"], ["C#"], salary: new SalaryRange(120000, 150000, SalaryPeriod.Year)),
            Make.Posting("adzuna:2", ["r2"], skillsOfSecond, city: null, remote: true, fullText: false, duplicateOf: "greenhouse:1"),
        ],
        [new AdzunaAggregate("r2", "net-developer", "histogram", """{"histogram":{"100000":7}}""")],
        [new JobBankMonth(new DateOnly(2026, 8, 1), "21232", "Software developers and programmers", 2, 3, 86600, 107000)]);

    private static async Task<string> Snapshot(TrackerDb db)
    {
        var postings = await db.Postings.AsNoTracking().OrderBy(p => p.Id)
            .Select(p => $"{p.Id}|{p.City}|{p.InScope}|{p.RoleFamily}|{p.Seniority}|{p.SalaryMin}|{p.SalaryPeriod}|{p.DuplicateOf}|{p.FirstSeenRun}|{p.LastSeenRun}")
            .ToListAsync();
        var counts = $"{await db.Runs.CountAsync()}|{await db.Skills.CountAsync()}|{await db.PostingSightings.CountAsync()}|{await db.PostingSkills.CountAsync()}|{await db.AdzunaAggregates.CountAsync()}|{await db.JobBankMonths.CountAsync()}";
        return string.Join("\n", postings.Append(counts));
    }

    [Fact]
    public async Task Rebuild_writes_every_table()
    {
        await using var db = await pg.OpenAsync();

        await new RebuildWriter(db).WriteAsync(Data("Azure"), CancellationToken.None);

        var first = await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "greenhouse:1");
        Assert.Equal(("dotnet", "Senior", 120000m, "Year", true), (first.RoleFamily, first.Seniority, first.SalaryMin!.Value, first.SalaryPeriod, first.InScope));
        var second = await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "adzuna:2");
        Assert.Equal("greenhouse:1", second.DuplicateOf);
        Assert.True(second.IsRemoteInCountry);
        Assert.Equal(3, await db.PostingSightings.CountAsync());
        Assert.Equal(2, await db.PostingSkills.CountAsync());
        Assert.Equal(2, await db.Runs.CountAsync());
        Assert.Contains("100000", (await db.AdzunaAggregates.SingleAsync()).Payload);
        Assert.Equal(86600m, (await db.JobBankMonths.SingleAsync()).WageMinMedian);
    }

    [Fact]
    public async Task Job_bank_region_totals_are_written_and_replaced()
    {
        await using var db = await pg.OpenAsync();
        var writer = new RebuildWriter(db);

        await writer.WriteAsync(Data("Azure") with { JobBankRegions = [new JobBankRegionMonth(new DateOnly(2026, 8, 1), "Kitchener–Waterloo–Barrie Region", 4, 6)] },
            CancellationToken.None);
        var row = await db.JobBankRegions.AsNoTracking().SingleAsync();
        Assert.Equal((new DateOnly(2026, 8, 1), "Kitchener–Waterloo–Barrie Region", 4, 6), (row.Month, row.EconomicRegion, row.Postings, row.Vacancies));

        await writer.WriteAsync(Data("Azure"), CancellationToken.None);
        Assert.Equal(0, await db.JobBankRegions.CountAsync());
    }

    [Fact]
    public async Task Rebuilding_twice_gives_identical_rows()
    {
        await using var db = await pg.OpenAsync();
        var writer = new RebuildWriter(db);

        await writer.WriteAsync(Data("Azure"), CancellationToken.None);
        var once = await Snapshot(db);
        await writer.WriteAsync(Data("Azure"), CancellationToken.None);

        Assert.Equal(once, await Snapshot(db));
    }

    [Fact]
    public async Task A_failed_rebuild_keeps_the_previous_data()
    {
        await using var db = await pg.OpenAsync();
        var writer = new RebuildWriter(db);
        await writer.WriteAsync(Data("Azure"), CancellationToken.None);
        var before = await Snapshot(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => writer.WriteAsync(Data("Cobol"), CancellationToken.None));

        Assert.Equal(before, await Snapshot(db));
    }
}
