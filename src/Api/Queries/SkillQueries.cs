using Api.Market;
using Tracker;
using Tracker.Analysis;
using Tracker.Interpreting;

namespace Api.Queries;

public sealed record SkillRow(string Skill, string Category, double Share, int Count, SkillLevel Level, double? PreviousShare, double? Change);

public sealed record SkillList(int Postings, IReadOnlyList<SkillRow> Skills);

public sealed record NamedCount(string Name, int Count);

public sealed record SalaryComparison(decimal? WithMedian, int WithCount, decimal? WithoutMedian, int WithoutCount);

public sealed record SkillDetail(string Skill, string Category, SkillLevel Level, double Share, int Count, int Postings,
    SalaryComparison Salary, IReadOnlyList<NamedCount> CoSkills, IReadOnlyList<NamedCount> Companies, IReadOnlyList<PostingRow> Examples);

public static class SkillQueries
{
    public const int MaxExamples = 50, MaxRelated = 10;

    public static SkillList List(MarketSnapshot data, FilterSet filters, TextKind text, string? category, MarketSettings settings)
    {
        var current = ByText(filters.Apply(data), text);
        var previous = PreviousWindow(data, filters, text);
        var rows = settings.Catalog.Skills
            .Where(s => string.IsNullOrEmpty(category) || s.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Select(s =>
            {
                var count = current.Count(p => p.Skills.Contains(s.Name));
                var share = current.Count == 0 ? 0 : (double)count / current.Count;
                double? before = previous is { Count: > 0 } ? (double)previous.Count(p => p.Skills.Contains(s.Name)) / previous.Count : null;
                return new SkillRow(s.Name, s.Category, share, count, settings.Profile.LevelOf(s.Name), before, before is { } b ? share - b : null);
            })
            .OrderByDescending(r => r.Share).ThenBy(r => r.Skill, StringComparer.Ordinal)
            .ToList();
        return new SkillList(current.Count, rows);
    }

    public static SkillDetail? Detail(MarketSnapshot data, FilterSet filters, TextKind text, string skill, MarketSettings settings)
    {
        var definition = settings.Catalog.Skills.FirstOrDefault(s => s.Name == skill);
        if (definition is null) return null;

        var postings = ByText(filters.Apply(data), text);
        var withSkill = postings.Where(p => p.Skills.Contains(skill)).ToList();
        var withoutSkill = postings.Where(p => !p.Skills.Contains(skill)).ToList();

        return new SkillDetail(skill, definition.Category, settings.Profile.LevelOf(skill),
            postings.Count == 0 ? 0 : (double)withSkill.Count / postings.Count, withSkill.Count, postings.Count,
            new SalaryComparison(Median(withSkill), withSkill.Count(p => p.Salary is not null),
                Median(withoutSkill), withoutSkill.Count(p => p.Salary is not null)),
            Top(withSkill.SelectMany(p => p.Skills).Where(s => s != skill)),
            [.. withSkill.GroupBy(p => CompanyName.Key(p.Company), StringComparer.Ordinal)
                .Select(g => new NamedCount(CompanyName.Display(g.Select(p => p.Company)), g.Count()))
                .OrderByDescending(n => n.Count).ThenBy(n => n.Name, StringComparer.Ordinal)
                .Take(MaxRelated)],
            [.. withSkill.OrderByDescending(p => p.Sightings.Min()).Take(MaxExamples).Select(p => PostingQueries.ToRow(p, settings.Profile))]);
    }

    internal static IReadOnlyList<AnalysisPosting> ByText(IReadOnlyList<AnalysisPosting> postings, TextKind text) => text switch
    {
        TextKind.Full => [.. postings.Where(p => p.IsFullText)],
        TextKind.Snippet => [.. postings.Where(p => !p.IsFullText)],
        _ => postings,
    };

    // The same-length window just before the current one, only once the archive covers all of it: a partly
    // covered window would compare a month of postings with a few days' worth.
    private static IReadOnlyList<AnalysisPosting>? PreviousWindow(MarketSnapshot data, FilterSet filters, TextKind text)
    {
        if (filters.WindowDays is not { } days || data.LatestRunAt is not { } latest || data.Runs.Count == 0) return null;
        var from = latest.AddDays(-2 * days);
        var to = latest.AddDays(-days);
        if (data.Runs.Min(r => r.StartedAt) > from) return null;
        return ByText(filters.ApplyBetween(data, from, to), text);
    }

    private static decimal? Median(IEnumerable<AnalysisPosting> postings)
    {
        var mids = postings.Where(p => p.Salary is not null).Select(p => p.Salary!.YearlyMidpoint).ToList();
        return mids.Count >= Analyzer.MinSalarySample ? Stats.Median(mids) : null;
    }

    private static IReadOnlyList<NamedCount> Top(IEnumerable<string> names) =>
        [.. names.GroupBy(n => n, StringComparer.Ordinal)
            .Select(g => new NamedCount(g.First(), g.Count()))
            .OrderByDescending(n => n.Count).ThenBy(n => n.Name, StringComparer.Ordinal)
            .Take(MaxRelated)];
}
