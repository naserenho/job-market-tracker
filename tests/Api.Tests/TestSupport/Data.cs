using Api;
using Api.Market;
using Tracker.Analysis;
using Tracker.Interpreting;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Api.Tests.TestSupport;

// Small hand-made market: runs on day 0 (2026-09-01) and later days as each test needs.
public static class Data
{
    public static readonly DateTimeOffset T0 = new(2026, 9, 1, 7, 0, 0, TimeSpan.FromHours(-4));
    public static DateTimeOffset Day(int d) => T0.AddDays(d);

    // Toronto, whichever region config/sources.json names.
    public static MarketSettings Settings { get; } = MarketSettings.Load(RepoConfig.Root, RepoConfig.PathOf("profile.example.json"), "unused", "toronto");

    public static AnalysisPosting P(string id, string[] skills, int[] seen, RoleFamily family = RoleFamily.Dotnet,
        Seniority seniority = Seniority.Senior, decimal? salary = null, bool fullText = true, string company = "Acme",
        string title = "Developer", bool gta = true, bool remote = false, string source = "greenhouse") =>
        new(id, source, company, title, $"https://x.test/{id}", gta ? "Toronto" : "Remote (Canada)", family, seniority,
            salary is { } s ? new SalaryRange(s, s, SalaryPeriod.Year) : null, fullText, false, skills.ToHashSet(),
            [.. seen.Select(Day)], gta, remote, gta ? "Toronto" : null);

    public static MarketSnapshot MarketOf(IReadOnlyList<AnalysisPosting> postings, params int[] runDays) =>
        new(postings, [.. runDays.Select(d => new RunInfo($"r{d}", Day(d)))], [], [], Day(100));
}
