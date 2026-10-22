using Tracker.Analysis;
using Tracker.Interpreting;
using Tracker.Reading;

namespace Tracker.Core.Tests.Analysis;

public class AnalyzerTests
{
    private static readonly SkillCatalog Catalog = new([
        new("C#", "language", ["C#"]), new("Azure", "cloud", ["Azure"]), new("Kubernetes", "devops", ["Kubernetes"]),
        new("React", "frontend", ["React"]), new("Java", "language", ["Java"])]);

    private static readonly Profile Profile = new(
        new HashSet<RoleFamily> { RoleFamily.Dotnet, RoleFamily.Fullstack },
        new Dictionary<string, SkillLevel> { ["C#"] = SkillLevel.Strong, ["React"] = SkillLevel.Working, ["Azure"] = SkillLevel.Learning });

    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 7, 0, 0, TimeSpan.FromHours(-4));

    private static DateTimeOffset Day(int d) => T0.AddDays(d);

    private static AnalysisPosting P(string id, string[] skills, int[] seenDays, RoleFamily family = RoleFamily.Dotnet, bool fullText = true,
        decimal? salary = null, Seniority seniority = Seniority.Senior, string company = "Acme", bool flagged = false, string source = "greenhouse") =>
        new(id, source, company, $"Job {id}", $"https://x.test/{id}", "Toronto", family, seniority,
            salary is { } s ? new SalaryRange(s, s, SalaryPeriod.Year) : null, fullText, flagged, skills.ToHashSet(), [.. seenDays.Select(Day)]);

    private static RunInfo[] Runs(params int[] days) => [.. days.Select(d => new RunInfo($"r{d}", Day(d)))];

    private static ReportModel Build(IReadOnlyList<AnalysisPosting> postings, RunInfo[] runs) =>
        new Analyzer(Profile, Catalog, 30).Build(postings, runs, [], ["a warning"], Day(99));

    // A posting in the window at a city (local) or remote in the country (city null, not local).
    private static AnalysisPosting At(string id, string? city, decimal? salary = null) =>
        P(id, ["C#"], [0], salary: salary) with { IsLocal = city is not null, IsRemoteInCountry = city is null, City = city };

    private static readonly AreaSettings Areas = new(
        new Dictionary<string, string>
        {
            ["Toronto"] = "Toronto area (GTA)", ["Mississauga"] = "Toronto area (GTA)", ["Ottawa"] = "Ottawa", ["London"] = "London",
        },
        "Other Ontario", "Remote (Canada)",
        [
            new JobBankRegionMonth(new DateOnly(2026, 7, 1), "Ottawa Region", 2, 3),
            new JobBankRegionMonth(new DateOnly(2026, 8, 1), "Ottawa Region", 3, 3),
            new JobBankRegionMonth(new DateOnly(2026, 8, 1), "Toronto Region", 40, 50),
        ]);

    private static AreaBreakdown AreasOf(IReadOnlyList<AnalysisPosting> postings) =>
        new Analyzer(Profile, Catalog, 30).Build(postings, Runs(0), [], [], Day(99), Areas).Areas!;

    [Fact]
    public void Areas_group_window_postings_by_area()
    {
        var areas = AreasOf([At("1", "Toronto"), At("2", "Mississauga"), At("3", "Toronto"), At("4", "Ottawa"), At("5", "Sudbury"), At("6", null)]);

        // Postings descending then name; an area with none still shows; remote last.
        Assert.Equal(
            [("Toronto area (GTA)", 3), ("Other Ontario", 1), ("Ottawa", 1), ("London", 0), ("Remote (Canada)", 1)],
            areas.Areas.Select(a => (a.Area, a.Postings)));
        Assert.Equal(0.5, areas.Areas[0].Share);
        Assert.Equal(0, areas.Areas.Single(a => a.Area == "London").Share);
    }

    [Fact]
    public void Area_median_needs_ten_salaries()
    {
        var nine = AreasOf([.. Enumerable.Range(0, 9).Select(i => At($"o{i}", "Ottawa", 100_000 + i * 10_000))]);
        var ten = AreasOf([.. Enumerable.Range(0, 10).Select(i => At($"o{i}", "Ottawa", 100_000 + i * 10_000))]);

        var ottawa9 = nine.Areas.Single(a => a.Area == "Ottawa");
        Assert.Equal((9, (decimal?)null), (ottawa9.WithSalary, ottawa9.MedianYearly));
        var ottawa10 = ten.Areas.Single(a => a.Area == "Ottawa");
        Assert.Equal((10, (decimal?)145_000), (ottawa10.WithSalary, ottawa10.MedianYearly));
    }

    [Fact]
    public void Job_bank_region_totals_sum_all_months() =>
        Assert.Equal([("Toronto Region", 40, 50), ("Ottawa Region", 5, 6)],
            AreasOf([At("1", "Toronto")]).JobBank.Select(j => (j.EconomicRegion, j.Postings, j.Vacancies)));

    // "Ontario, Canada" names no town: its own row, not Other Ontario; remote roles in the province go to the remote row.
    [Fact]
    public void Postings_naming_no_city_get_their_own_row_or_go_remote()
    {
        var settings = Areas with { NoCity = "Ontario", NoCityArea = "Ontario, no city given" };
        var areas = new Analyzer(Profile, Catalog, 30).Build(
            [At("1", "Toronto"), At("2", "Ontario"), At("3", "Ontario") with { IsRemoteInCountry = true }, At("4", "Sudbury")],
            Runs(0), [], [], Day(99), settings).Areas!;

        Assert.Equal(
            [("Other Ontario", 1), ("Toronto area (GTA)", 1), ("London", 0), ("Ottawa", 0), ("Ontario, no city given", 1), ("Remote (Canada)", 1)],
            areas.Areas.Select(a => (a.Area, a.Postings)));
    }

    [Fact]
    public void No_area_settings_means_no_areas() =>
        Assert.Null(Build([P("1", ["C#"], [0])], Runs(0)).Areas);

    [Fact]
    public void Demand_counts_full_text_postings_in_target_families_within_the_window()
    {
        var model = Build([
            P("1", ["C#", "Azure"], [0]),
            P("2", ["C#", "Kubernetes"], [0]),
            P("3", ["Azure"], [0], fullText: false),
            P("4", ["Java"], [0], family: RoleFamily.Other),
            P("5", ["Java"], [-40]),
        ], Runs(-40, 0));

        var csharp = model.TopSkills.Single(s => s.Skill == "C#");
        Assert.Equal((1.0, 2, SkillLevel.Strong), (csharp.FullTextShare, csharp.Count, csharp.Level));
        var azure = model.TopSkills.Single(s => s.Skill == "Azure");
        Assert.Equal((0.5, 1.0), (azure.FullTextShare, azure.SnippetShare));
        Assert.DoesNotContain(model.TopSkills, s => s.Skill == "Java");
        Assert.Equal(3, model.Snapshot.InWindow);
        Assert.Equal(new[] { "a warning" }, model.Warnings);
    }

    [Fact]
    public void Gaps_weight_demand_by_the_missing_level_and_strengths_by_the_level()
    {
        var model = Build([P("1", ["C#", "Azure", "Kubernetes"], [0]), P("2", ["C#", "Kubernetes"], [0])], Runs(0));

        Assert.Equal(new[] { "Kubernetes", "Azure" }, model.Gaps.Select(g => g.Skill));
        Assert.Equal(1.0, model.Gaps[0].Score, 3);
        Assert.Equal(0.35, model.Gaps[1].Score, 3);
        Assert.Equal(new[] { "C#", "Azure" }, model.Strengths.Select(s => s.Skill));
        Assert.Equal(0.15, model.Strengths[1].Score, 3);
    }

    [Fact]
    public void Matches_rank_open_postings_by_the_share_of_skills_you_have()
    {
        var model = Build([
            P("good", ["C#", "React", "Azure"], [0]),
            P("stretch", ["C#", "Kubernetes", "Java"], [0]),
            P("tiny", ["C#", "React"], [0]),
            P("closed", ["C#", "React", "Azure"], [-10]),
        ], Runs(-10, 0));

        Assert.Equal(new[] { "Job good", "Job stretch" }, model.Matches.Select(m => m.Title));
        Assert.Equal(new[] { "good", "stretch" }, model.Matches.Select(m => m.PostingId));
        Assert.Equal(2.0 / 3, model.Matches[0].Score, 3);
        Assert.Empty(model.Matches[0].Missing);
        Assert.Equal(new[] { "Java", "Kubernetes" }, model.Matches[1].Missing.Order());
    }

    [Fact]
    public void Salary_cells_need_five_postings()
    {
        var postings = Enumerable.Range(0, 5).Select(i => P($"s{i}", ["C#"], [0], salary: 100_000 + i * 10_000)).ToList();
        postings.Add(P("lead", ["C#"], [0], seniority: Seniority.Lead, salary: 150_000));

        var model = Build(postings, Runs(0));

        var senior = model.Salaries.Single(c => c.Seniority == Seniority.Senior);
        Assert.Equal(5, senior.Count);
        Assert.Equal(120_000m, senior.Median);
        Assert.Equal(110_000m, senior.P25);
        Assert.Equal(130_000m, senior.P75);
        var lead = model.Salaries.Single(c => c.Seniority == Seniority.Lead);
        Assert.Equal(1, lead.Count);
        Assert.Null(lead.Median);
    }

    [Fact]
    public void Companies_and_snapshot()
    {
        var model = Build([
            P("1", ["C#"], [-10, 0], company: "Acme"),
            P("2", ["C#"], [0], company: "ACME"),
            P("3", ["C#"], [0], company: "Globex", flagged: true, source: "adzuna", fullText: false),
        ], Runs(-10, 0));

        Assert.Equal(("ACME", 2), (model.Companies[0].Company, model.Companies[0].Postings)); // one of each spelling: ordinal order decides
        var s = model.Snapshot;
        Assert.Equal((3, 2, 1, 2, 10), (s.ActiveNow, s.NewSinceLastRun, s.CitizenshipFlagged, s.Runs, s.HistoryDays));
        Assert.Equal(2, s.BySource["greenhouse"]);
        Assert.Equal(1, s.BySource["adzuna"]);
        Assert.Equal("r0", model.LatestRunId);
    }

    [Fact]
    public void Trends_appear_after_two_weeks_of_history()
    {
        Assert.Null(Build([P("1", ["C#"], [0])], Runs(-7, 0)).Trends);

        var model = Build([P("old", ["Java", "C#"], [-45]), P("new", ["C#"], [0]), P("both", ["C#", "Azure"], [-45, 0])], Runs(-45, 0));

        Assert.NotNull(model.Trends);
        Assert.All(model.Trends.Weekly, w => Assert.Equal(DayOfWeek.Monday, w.WeekStart.DayOfWeek));
        Assert.Equal(2, model.Trends.Weekly[0].Active);
        Assert.Equal(2, model.Trends.Weekly[^1].Active);
        var java = model.Trends.Moves.Single(m => m.Skill == "Java");
        Assert.Equal((0.5, 0.0), (java.Previous, java.Current));
    }

    [Fact]
    public void Company_variants_count_as_one_company()
    {
        var model = Build([
            P("1", ["C#"], [0], company: "Acme"), P("2", ["C#"], [0], company: "Acme Inc."),
            P("3", ["C#"], [0], company: " Acme Inc. "), P("4", ["C#"], [0], company: "Globex"),
        ], Runs(0));

        Assert.Equal(new[] { ("Acme Inc.", 3), ("Globex", 1) }, model.Companies.Select(c => (c.Company, c.Postings)));
    }
}
