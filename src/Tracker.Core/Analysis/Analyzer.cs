using Tracker.Interpreting;
using Tracker.Reading;

namespace Tracker.Analysis;

public sealed record AnalysisPosting(string Id, string Source, string Company, string Title, string Url, string Location,
    RoleFamily Family, Seniority Seniority, SalaryRange? Salary, bool IsFullText, bool CitizenshipOrClearance,
    IReadOnlySet<string> Skills, IReadOnlyList<DateTimeOffset> Sightings,
    bool IsLocal = true, bool IsRemoteInCountry = false, string? City = null);

public sealed record RunInfo(string RunId, DateTimeOffset StartedAt);

public sealed class Analyzer(Profile profile, SkillCatalog catalog, int windowDays)
{
    public const int TopSkillCount = 25, TopGapCount = 15, TopStrengthCount = 10, TopMatchCount = 20, TopCompanyCount = 20;
    public const int MinSalarySample = 5, MinTrendDays = 14, MinMatchSkills = 3, MinAreaSalarySample = 10;

    // inScope: in-scope, non-duplicate postings (all families); runs: at least one.
    // areas: only for a region that defines Local.Areas; the dashboard passes none.
    public ReportModel Build(IReadOnlyList<AnalysisPosting> inScope, IReadOnlyList<RunInfo> runs, IReadOnlyList<JobBankMonth> jobBank,
        IReadOnlyList<string> warnings, DateTimeOffset generatedAt, AreaSettings? areas = null)
    {
        var ordered = runs.OrderBy(r => r.StartedAt).ToList();
        var latest = ordered[^1];
        var windowStart = latest.StartedAt.AddDays(-windowDays);

        var targetAll = inScope.Where(p => profile.TargetFamilies.Contains(p.Family)).ToList();
        var window = targetAll.Where(p => p.Sightings.Any(s => s >= windowStart)).ToList();
        var full = window.Where(p => p.IsFullText).ToList();
        var snippets = window.Where(p => !p.IsFullText).ToList();
        var active = window.Where(p => p.Sightings.Contains(latest.StartedAt)).ToList();

        var demand = catalog.Skills
            .Select(s => new SkillDemand(s.Name, s.Category, Share(full, s.Name), Share(snippets, s.Name),
                full.Count(p => p.Skills.Contains(s.Name)), profile.LevelOf(s.Name)))
            .ToList();

        return new ReportModel(
            generatedAt, latest.RunId, latest.StartedAt, windowDays,
            BuildSnapshot(window, full, snippets, active, ordered),
            [.. demand.Where(d => d.Count > 0).OrderByDescending(d => d.FullTextShare).ThenBy(d => d.Skill, StringComparer.Ordinal).Take(TopSkillCount)],
            Rank(demand, d => 1 - Profile.Weight(d.Level), TopGapCount),
            Rank(demand, d => Profile.Weight(d.Level), TopStrengthCount),
            SalaryCells(window),
            TopCompanies(window),
            BestMatches(active, demand.ToDictionary(d => d.Skill, d => d.FullTextShare, StringComparer.Ordinal)),
            BuildTrends(targetAll, ordered),
            [.. jobBank.OrderByDescending(j => j.Month).ThenBy(j => j.NocCode, StringComparer.Ordinal)],
            warnings,
            areas is null ? null : Areas(window, areas));
    }

    // Every configured area and the catch-all appear, even with no postings; remote roles are the last row.
    private static AreaBreakdown Areas(IReadOnlyList<AnalysisPosting> window, AreaSettings settings)
    {
        string AreaOf(AnalysisPosting p) =>
            !p.IsLocal ? settings.RemoteArea
            : settings.NoCity is not null && p.City == settings.NoCity ? (p.IsRemoteInCountry ? settings.RemoteArea : settings.NoCityArea)
            : settings.Map.TryGetValue(p.City ?? "", out var area) ? area : settings.OtherArea;
        var groups = window.GroupBy(AreaOf).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        AreaCount Count(string area)
        {
            var postings = groups.GetValueOrDefault(area) ?? [];
            var salaries = postings.Where(p => p.Salary is not null).Select(p => p.Salary!.YearlyMidpoint).ToList();
            return new AreaCount(area, postings.Count, window.Count == 0 ? 0 : (double)postings.Count / window.Count, salaries.Count,
                salaries.Count >= MinAreaSalarySample ? Stats.Median(salaries) : null);
        }

        var local = settings.Map.Values.Append(settings.OtherArea).Distinct(StringComparer.Ordinal).Select(Count)
            .OrderByDescending(a => a.Postings).ThenBy(a => a.Area, StringComparer.Ordinal);
        var jobBank = settings.JobBank.GroupBy(j => j.EconomicRegion, StringComparer.Ordinal)
            .Select(g => new JobBankRegionTotal(g.Key, g.Sum(j => j.Postings), g.Sum(j => j.Vacancies)))
            .OrderByDescending(j => j.Postings).ThenBy(j => j.EconomicRegion, StringComparer.Ordinal);
        AreaCount[] noCity = settings.NoCity is null ? [] : [Count(settings.NoCityArea)];
        return new AreaBreakdown([.. local, .. noCity, Count(settings.RemoteArea)], [.. jobBank]);
    }

    private static double Share(IReadOnlyList<AnalysisPosting> postings, string skill) =>
        postings.Count == 0 ? 0 : (double)postings.Count(p => p.Skills.Contains(skill)) / postings.Count;

    private static IReadOnlyList<SkillScore> Rank(IEnumerable<SkillDemand> demand, Func<SkillDemand, double> factor, int take) =>
        [.. demand.Select(d => new SkillScore(d.Skill, d.Category, d.FullTextShare, d.Level, d.FullTextShare * factor(d)))
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score).ThenBy(s => s.Skill, StringComparer.Ordinal)
            .Take(take)];

    private static Snapshot BuildSnapshot(IReadOnlyList<AnalysisPosting> window, IReadOnlyList<AnalysisPosting> full,
        IReadOnlyList<AnalysisPosting> snippets, IReadOnlyList<AnalysisPosting> active, IReadOnlyList<RunInfo> runs)
    {
        var latest = runs[^1].StartedAt;
        return new Snapshot(
            ActiveNow: active.Count,
            NewSinceLastRun: runs.Count > 1 ? active.Count(p => p.Sightings.Min() == latest) : 0,
            InWindow: window.Count,
            FullText: full.Count,
            Snippets: snippets.Count,
            WithSalary: window.Count(p => p.Salary is not null),
            CitizenshipFlagged: window.Count(p => p.CitizenshipOrClearance),
            Runs: runs.Count,
            HistoryDays: (int)Math.Round((latest - runs[0].StartedAt).TotalDays),
            BySource: window.GroupBy(p => p.Source).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()));
    }

    public static IReadOnlyList<SalaryCell> SalaryCells(IReadOnlyList<AnalysisPosting> window) =>
        [.. window.Where(p => p.Salary is not null)
            .GroupBy(p => (p.Family, p.Seniority))
            .Select(g =>
            {
                var mids = g.Select(p => p.Salary!.YearlyMidpoint).Order().ToList();
                var enough = mids.Count >= MinSalarySample;
                return new SalaryCell(g.Key.Family, g.Key.Seniority, mids.Count,
                    enough ? Stats.Percentile(mids, 0.5) : null,
                    enough ? Stats.Percentile(mids, 0.25) : null,
                    enough ? Stats.Percentile(mids, 0.75) : null);
            })
            .OrderBy(c => c.Family).ThenBy(c => c.Seniority)];

    private static IReadOnlyList<CompanyCount> TopCompanies(IReadOnlyList<AnalysisPosting> window) =>
        [.. window.GroupBy(p => CompanyName.Key(p.Company), StringComparer.Ordinal)
            .Select(g => new CompanyCount(CompanyName.Display(g.Select(p => p.Company)), g.Count()))
            .OrderByDescending(c => c.Postings).ThenBy(c => c.Company, StringComparer.Ordinal)
            .Take(TopCompanyCount)];

    private IReadOnlyList<PostingMatch> BestMatches(IReadOnlyList<AnalysisPosting> active, IReadOnlyDictionary<string, double> demand) =>
        [.. active.Where(p => p.Skills.Count >= MinMatchSkills)
            .Select(p =>
            {
                var covered = p.Skills.Count(s => profile.LevelOf(s) >= SkillLevel.Working);
                var missing = p.Skills.Where(s => profile.LevelOf(s) == SkillLevel.None)
                    .OrderByDescending(s => demand.GetValueOrDefault(s)).ThenBy(s => s, StringComparer.Ordinal)
                    .Take(3).ToList();
                return new PostingMatch(p.Title, p.Company, p.Url, p.Location, (double)covered / p.Skills.Count, p.Skills.Count,
                    missing, p.Salary, p.CitizenshipOrClearance, p.Id);
            })
            .OrderByDescending(m => m.Score).ThenByDescending(m => m.SkillCount).ThenBy(m => m.Title, StringComparer.Ordinal)
            .Take(TopMatchCount)];

    private TrendData? BuildTrends(IReadOnlyList<AnalysisPosting> targetAll, IReadOnlyList<RunInfo> runs)
    {
        var first = runs[0].StartedAt;
        var latest = runs[^1].StartedAt;
        if ((latest - first).TotalDays < MinTrendDays) return null;

        var weeks = new List<WeekCount>();
        for (var week = WeekStart(first); week <= WeekStart(latest); week = week.AddDays(7))
        {
            var (start, end) = (week, week.AddDays(7));
            weeks.Add(new WeekCount(start, targetAll.Count(p => p.Sightings.Any(s =>
            {
                var day = DateOnly.FromDateTime(s.Date);
                return day >= start && day < end;
            }))));
        }

        var currentStart = latest.AddDays(-windowDays);
        var previousStart = currentStart.AddDays(-windowDays);
        var current = targetAll.Where(p => p.IsFullText && p.Sightings.Any(s => s >= currentStart)).ToList();
        var previous = targetAll.Where(p => p.IsFullText && p.Sightings.Any(s => s >= previousStart && s < currentStart)).ToList();
        IReadOnlyList<SkillMove> moves = previous.Count == 0
            ? []
            : [.. catalog.Skills.Select(s => new SkillMove(s.Name, Share(previous, s.Name), Share(current, s.Name)))
                .Where(m => m.Previous > 0 || m.Current > 0)
                .OrderByDescending(m => Math.Abs(m.Current - m.Previous)).ThenBy(m => m.Skill, StringComparer.Ordinal)
                .Take(10)];

        return new TrendData(weeks, moves);
    }

    private static DateOnly WeekStart(DateTimeOffset time)
    {
        var day = DateOnly.FromDateTime(time.Date);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }
}
