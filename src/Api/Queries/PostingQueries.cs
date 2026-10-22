using Api.Market;
using Tracker.Analysis;
using Tracker.Interpreting;

namespace Api.Queries;

public sealed record PostingRow(string Id, string Title, string Company, string Location, string Url,
    decimal? SalaryMin, decimal? SalaryMax, string? SalaryPeriod, double Match, DateTimeOffset FirstSeen,
    IReadOnlyList<string> Skills, bool CitizenshipOrClearance, string Source, bool IsFullText);

public sealed record PostingPage(int Total, int Page, int PageSize, IReadOnlyList<PostingRow> Items);

public static class PostingQueries
{
    // Share of the posting's skills the owner has at working level or better (the report's match score).
    public static double Match(AnalysisPosting p, Profile profile) =>
        p.Skills.Count == 0 ? 0 : (double)p.Skills.Count(s => profile.LevelOf(s) >= SkillLevel.Working) / p.Skills.Count;

    public static PostingRow ToRow(AnalysisPosting p, Profile profile) => new(
        p.Id, p.Title, p.Company, p.Location, p.Url, p.Salary?.Min, p.Salary?.Max, p.Salary?.Period.ToString(),
        Match(p, profile), p.Sightings.Count == 0 ? DateTimeOffset.MinValue : p.Sightings.Min(),
        [.. p.Skills.Order(StringComparer.Ordinal)], p.CitizenshipOrClearance, p.Source, p.IsFullText);

    public static PostingPage Page(MarketSnapshot data, FilterSet filters, string? q, string? skill, string? company,
        PostingSort sort, int page, int pageSize, Profile profile)
    {
        var companyKey = string.IsNullOrWhiteSpace(company) ? null : CompanyName.Key(company);
        var rows = filters.Apply(data)
            .Where(p => string.IsNullOrWhiteSpace(q)
                || p.Title.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
                || p.Company.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(p => string.IsNullOrEmpty(skill) || p.Skills.Contains(skill))
            .Where(p => companyKey is null || CompanyName.Key(p.Company) == companyKey)
            .Select(p => (Posting: p, Row: ToRow(p, profile)))
            .ToList();

        IEnumerable<(AnalysisPosting Posting, PostingRow Row)> ordered = sort switch
        {
            // Like the report's best matches: a posting needs a few detected skills before its score means much,
            // so thin ones (often Adzuna snippets naming just "C#") go after the rest.
            PostingSort.Match => rows.OrderByDescending(r => r.Posting.Skills.Count >= Analyzer.MinMatchSkills)
                .ThenByDescending(r => r.Row.Match).ThenByDescending(r => r.Posting.Skills.Count).ThenByDescending(r => r.Row.FirstSeen),
            PostingSort.Salary => rows.OrderByDescending(r => r.Posting.Salary?.YearlyMidpoint ?? -1).ThenByDescending(r => r.Row.FirstSeen),
            _ => rows.OrderByDescending(r => r.Row.FirstSeen).ThenBy(r => r.Row.Title, StringComparer.Ordinal),
        };
        return new PostingPage(rows.Count, page, pageSize, [.. ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(r => r.Row)]);
    }
}
