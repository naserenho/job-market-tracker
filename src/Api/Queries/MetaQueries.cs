using Api.Market;
using Tracker.Analysis;
using Tracker.Interpreting;

namespace Api.Queries;

public sealed record MetaView(string? LatestRunId, DateTimeOffset? LatestRunAt, int Runs, int HistoryDays,
    IReadOnlyList<string> TargetFamilies, IReadOnlyList<string> Families, IReadOnlyList<string> Seniorities,
    IReadOnlyList<string> Categories, IReadOnlyList<string> Companies, bool Stale, RegionView Region);

// What the dashboard needs to speak the region: its names, currency and how to format numbers and dates.
public sealed record RegionView(string Name, string Title, string LocalLabel, string RemoteLabel, string Currency, string MoneyPrefix,
    string Culture, string TimeZone);

public static class MetaQueries
{
    public const int MaxCompanies = 50;

    public static MetaView Meta(MarketSnapshot data, MarketSettings settings, bool stale)
    {
        var latest = data.Runs.OrderBy(r => r.StartedAt).LastOrDefault();
        var history = data.Runs.Count == 0 ? 0 : (int)Math.Round((data.Runs.Max(r => r.StartedAt) - data.Runs.Min(r => r.StartedAt)).TotalDays);
        return new MetaView(latest?.RunId, latest?.StartedAt, data.Runs.Count, history,
            [.. settings.Profile.TargetFamilies.Order().Select(f => f.ToString())],
            Enum.GetNames<RoleFamily>(), Enum.GetNames<Seniority>(),
            [.. settings.Catalog.Skills.Select(s => s.Category).Distinct().Order(StringComparer.Ordinal)],
            [.. data.Postings.GroupBy(p => CompanyName.Key(p.Company), StringComparer.Ordinal)
                .Select(g => (Name: CompanyName.Display(g.Select(p => p.Company)), Count: g.Count()))
                .OrderByDescending(c => c.Count).ThenBy(c => c.Name, StringComparer.Ordinal)
                .Take(MaxCompanies).Select(c => c.Name)],
            stale,
            new RegionView(settings.Region.Name, settings.Region.Title, settings.Region.LocalLabel, settings.Region.RemoteLabel,
                settings.Region.Salary.Currency, settings.Region.Salary.MoneyPrefix, settings.Region.Culture, settings.Region.TimeZone));
    }

    // The report's model, computed over the filtered families, seniorities, places and window.
    public static ReportModel? Overview(MarketSnapshot data, FilterSet filters, MarketSettings settings, DateTimeOffset now)
    {
        if (data.Runs.Count == 0) return null;
        var windowDays = filters.WindowDays
            ?? (int)Math.Ceiling((data.Runs.Max(r => r.StartedAt) - data.Runs.Min(r => r.StartedAt)).TotalDays) + 1;
        var postings = (filters with { WindowDays = null }).Apply(data);
        var profile = new Profile(filters.Families, settings.Profile.Skills);
        return new Analyzer(profile, settings.Catalog, windowDays).Build(postings, data.Runs, data.JobBank, data.Warnings, now);
    }
}
