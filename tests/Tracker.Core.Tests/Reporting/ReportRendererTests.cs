using Tracker.Analysis;
using Tracker.Interpreting;
using Tracker.Reading;
using Tracker.Reporting;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Reporting;

public class ReportRendererTests
{
    internal static ReportModel Model(TrendData? trends = null, IReadOnlyList<string>? warnings = null) => new(
        GeneratedAt: new DateTimeOffset(2026, 10, 2, 7, 5, 0, TimeSpan.FromHours(-4)),
        LatestRunId: "2026-10-02_0700",
        LatestRunAt: new DateTimeOffset(2026, 10, 2, 7, 0, 0, TimeSpan.FromHours(-4)),
        WindowDays: 30,
        Snapshot: new Snapshot(ActiveNow: 812, NewSinceLastRun: 64, InWindow: 1204, FullText: 410, Snippets: 794, WithSalary: 602,
            CitizenshipFlagged: 9, Runs: 3, HistoryDays: 14, BySource: new Dictionary<string, int> { ["adzuna"] = 794, ["greenhouse"] = 410 }),
        TopSkills: [new SkillDemand("C#", "language", 0.62, 0.4, 254, SkillLevel.Strong), new SkillDemand("Kubernetes", "devops", 0.31, 0.1, 127, SkillLevel.None)],
        Gaps: [new SkillScore("Kubernetes", "devops", 0.31, SkillLevel.None, 0.31)],
        Strengths: [new SkillScore("C#", "language", 0.62, SkillLevel.Strong, 0.62)],
        Salaries: [new SalaryCell(RoleFamily.Dotnet, Seniority.Senior, 12, 128_000, 115_000, 140_000), new SalaryCell(RoleFamily.LeadManager, Seniority.Manager, 2, null, null, null)],
        Companies: [new CompanyCount("<script>alert(1)</script> Corp", 7)],
        Matches: [
            new PostingMatch("Senior .NET Developer", "Acme", "https://jobs.example/1", "Toronto", 0.8, 5, ["Kubernetes"], new SalaryRange(120_000, 150_000, SalaryPeriod.Year), true),
            new PostingMatch("Odd link", "Globex", "javascript:alert(1)", "Remote (Canada)", 0.5, 4, [], null, false)],
        Trends: trends,
        JobBank: [new JobBankMonth(new DateOnly(2026, 8, 1), "21232", "Software developers and programmers", 115, 140, 83_200, 104_000)],
        Warnings: warnings ?? []);

    [Fact]
    public void Fragment_follows_the_page_contract()
    {
        var html = ReportRenderer.RenderFragment(Model(), RepoConfig.Toronto);

        Assert.StartsWith("<title>GTA Job Market</title>", html);
        // Whole tag names only: <header> is fine, <head> is not.
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"<(?:!doctype|html|head|body|script)[\s>]", System.Text.RegularExpressions.RegexOptions.IgnoreCase), html);
        Assert.Contains(":root:not([data-theme=\"light\"])", html);
        Assert.Contains(":root[data-theme=\"dark\"]", html);
        Assert.Contains("color-scheme:dark", html);
        Assert.Contains("background:var(--bg)", html);
    }

    [Fact]
    public void Area_section_renders_for_a_region_with_areas()
    {
        var model = Model() with
        {
            Areas = new AreaBreakdown(
                [new AreaCount("Toronto area (GTA)", 900, 0.75, 400, 150_000), new AreaCount("Ottawa", 60, 0.05, 4, null),
                 new AreaCount("Remote (Canada)", 240, 0.2, 100, 170_000)],
                [new JobBankRegionTotal("Kitchener–Waterloo–Barrie Region", 18, 20)]),
        };

        var html = ReportRenderer.RenderFragment(model, RepoConfig.Ontario);
        var start = html.IndexOf("id=\"areas\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var section = html[start..html.IndexOf("</section>", start, StringComparison.Ordinal)];

        Assert.Contains("Where the jobs are", section);
        Assert.Contains("Your target roles in the last 30 days, by area. A posting listing several cities counts once, under the first.", section);
        Assert.Contains("province-wide search leans towards Toronto, so company boards and Job Bank carry the smaller cities.", section);
        Assert.Contains("Toronto area (GTA)", section);
        Assert.Contains("75%", section);
        Assert.Contains("$150k", section);
        Assert.Contains("—", section);
        Assert.Contains("Kitchener–Waterloo–Barrie Region", section);
        // Right after the snapshot row, before the skills.
        Assert.True(start < html.IndexOf("id=\"skills\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Toronto_report_has_no_area_section() =>
        Assert.DoesNotContain("id=\"areas\"", ReportRenderer.RenderFragment(Model(), RepoConfig.Toronto));

    [Fact]
    public void Job_bank_note_names_the_economic_region_or_the_province()
    {
        Assert.Contains("(NOC 2021) in the Toronto Region.", ReportRenderer.RenderFragment(Model(), RepoConfig.Toronto));
        Assert.Contains("(NOC 2021) in Ontario.", ReportRenderer.RenderFragment(Model(), RepoConfig.Ontario));
    }

    // The method note names the board types the region's file actually uses.
    [Fact]
    public void Method_names_the_board_types_the_region_uses()
    {
        Assert.Contains("public Greenhouse, Lever and Ashby job boards of tech employers hiring in GTA",
            ReportRenderer.RenderFragment(Model(), RepoConfig.Toronto));
        Assert.Contains("public Greenhouse, Lever, Ashby, Workable and SmartRecruiters job boards of tech employers hiring in Ontario",
            ReportRenderer.RenderFragment(Model(), RepoConfig.Ontario));
    }

    [Fact]
    public void Dynamic_text_is_escaped_and_unsafe_links_are_dropped()
    {
        var html = ReportRenderer.RenderFragment(Model(), RepoConfig.Toronto);

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; Corp", html);
        Assert.DoesNotContain("href=\"javascript:", html);
        Assert.Contains("href=\"https://jobs.example/1\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
    }

    [Theory]
    [InlineData("812")]
    [InlineData("62%")]
    [InlineData("$128k")]
    [InlineData("$115k–$140k")]
    [InlineData("not enough data")]
    [InlineData("80% match")]
    [InlineData("Missing: Kubernetes")]
    [InlineData("$120k–$150k")]
    [InlineData("Software developers and programmers")]
    [InlineData("Jobs data partly by")]
    [InlineData("Your biggest gap")]
    public void Sections_show_the_numbers(string expected) =>
        Assert.Contains(expected, ReportRenderer.RenderFragment(Model(), RepoConfig.Toronto));

    [Fact]
    public void Trends_section_only_with_trend_data()
    {
        Assert.DoesNotContain("id=\"trends\"", ReportRenderer.RenderFragment(Model(), RepoConfig.Toronto));

        var html = ReportRenderer.RenderFragment(Model(new TrendData(
            [new WeekCount(new DateOnly(2026, 9, 14), 700), new WeekCount(new DateOnly(2026, 9, 21), 760), new WeekCount(new DateOnly(2026, 9, 28), 812)],
            [new SkillMove("Azure", 0.2, 0.3)])), RepoConfig.Toronto);

        Assert.Contains("id=\"trends\"", html);
        Assert.Contains("<svg", html);
        Assert.Contains("Azure", html);
    }

    [Fact]
    public void One_day_of_history_is_singular_and_the_time_is_not_double_punctuated()
    {
        var model = Model() with { Snapshot = Model().Snapshot with { HistoryDays = 1 } };
        var html = ReportRenderer.RenderFragment(model, RepoConfig.Toronto);
        Assert.Contains(">1 day<", html);
        Assert.DoesNotContain("..", html.Replace("...", ""));
    }

    [Fact]
    public void Warnings_are_listed() =>
        Assert.Contains("Inbox file a.txt has no Company line.", ReportRenderer.RenderFragment(Model(warnings: ["Inbox file a.txt has no Company line."]), RepoConfig.Toronto));

    [Fact]
    public void Document_wraps_the_fragment()
    {
        var doc = ReportRenderer.RenderDocument("<p>x</p>");
        Assert.StartsWith("<!doctype html>", doc);
        Assert.Contains("<meta charset=\"utf-8\">", doc);
        Assert.Contains("<p>x</p>", doc);
    }

    [Fact]
    public void The_report_uses_the_region_title_labels_and_currency()
    {
        var html = ReportRenderer.RenderFragment(Model(), RepoConfig.AbuDhabi);

        Assert.Contains("<title>Abu Dhabi Job Market</title>", html);
        Assert.Contains("Abu Dhabi · Remote (UAE) · updated", html);
        Assert.Contains("Salary (AED, yearly)", html);
        Assert.Contains("AED 128k", html);           // money uses the region's prefix
        Assert.Contains("12 months a year", html);   // the UAE pays monthly
        Assert.DoesNotContain("Job Bank", html);     // the region has no Job Bank
        Assert.DoesNotContain("Adzuna", html);       // nor Adzuna, so no attribution either
        Assert.DoesNotContain("Canadian", html);
    }

    [Fact]
    public void Toronto_keeps_its_title_currency_and_sources()
    {
        var html = ReportRenderer.RenderFragment(Model(), RepoConfig.Toronto);

        Assert.Contains("<title>GTA Job Market</title>", html);
        Assert.Contains("GTA · Remote (Canada) · updated", html);
        Assert.Contains("Salary (CAD, yearly)", html);
        Assert.Contains("$128k", html);
        Assert.Contains("Job Bank", html);
        Assert.Contains("Adzuna", html);
    }
}
