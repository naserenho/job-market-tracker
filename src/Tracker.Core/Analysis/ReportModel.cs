using Tracker.Interpreting;
using Tracker.Reading;

namespace Tracker.Analysis;

public sealed record ReportModel(
    DateTimeOffset GeneratedAt,
    string LatestRunId,
    DateTimeOffset LatestRunAt,
    int WindowDays,
    Snapshot Snapshot,
    IReadOnlyList<SkillDemand> TopSkills,
    IReadOnlyList<SkillScore> Gaps,
    IReadOnlyList<SkillScore> Strengths,
    IReadOnlyList<SalaryCell> Salaries,
    IReadOnlyList<CompanyCount> Companies,
    IReadOnlyList<PostingMatch> Matches,
    TrendData? Trends,
    IReadOnlyList<JobBankMonth> JobBank,
    IReadOnlyList<string> Warnings,
    AreaBreakdown? Areas = null);

public sealed record Snapshot(int ActiveNow, int NewSinceLastRun, int InWindow, int FullText, int Snippets, int WithSalary,
    int CitizenshipFlagged, int Runs, int HistoryDays, IReadOnlyDictionary<string, int> BySource);

public sealed record SkillDemand(string Skill, string Category, double FullTextShare, double SnippetShare, int Count, SkillLevel Level);

public sealed record SkillScore(string Skill, string Category, double Demand, SkillLevel Level, double Score);

public sealed record SalaryCell(RoleFamily Family, Seniority Seniority, int Count, decimal? Median, decimal? P25, decimal? P75);

public sealed record CompanyCount(string Company, int Postings);

public sealed record PostingMatch(string Title, string Company, string Url, string Location, double Score, int SkillCount,
    IReadOnlyList<string> Missing, SalaryRange? Salary, bool CitizenshipOrClearance, string PostingId = "");

public sealed record TrendData(IReadOnlyList<WeekCount> Weekly, IReadOnlyList<SkillMove> Moves);

public sealed record WeekCount(DateOnly WeekStart, int Active);

public sealed record SkillMove(string Skill, double Previous, double Current);

// "Where the jobs are", for a region that groups its places into areas (Ontario): place shown → area, the names of the
// catch-all and remote rows, and Job Bank's postings per economic region. NoCity: the place shown when a posting names no
// town ("Ontario, Canada"); those get their own row, NoCityArea, or the remote row when they're remote.
public sealed record AreaSettings(IReadOnlyDictionary<string, string> Map, string OtherArea, string RemoteArea, IReadOnlyList<JobBankRegionMonth> JobBank,
    string? NoCity = null, string NoCityArea = "No city given");

public sealed record AreaCount(string Area, int Postings, double Share, int WithSalary, decimal? MedianYearly);

public sealed record JobBankRegionTotal(string EconomicRegion, int Postings, int Vacancies);

public sealed record AreaBreakdown(IReadOnlyList<AreaCount> Areas, IReadOnlyList<JobBankRegionTotal> JobBank);
