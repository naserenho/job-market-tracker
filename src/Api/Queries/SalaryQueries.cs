using Api.Market;
using Tracker.Analysis;
using Tracker.Interpreting;

namespace Api.Queries;

public sealed record Bucket(decimal From, decimal To, int Count);

public sealed record SalaryView(int Postings, int WithSalary, IReadOnlyList<SalaryCell> Cells, IReadOnlyList<Bucket> Histogram, IReadOnlyList<PostingRow> Examples);

public static class SalaryQueries
{
    public const decimal BucketWidth = 20_000;
    public const int MaxExamples = 100;

    public static SalaryView View(MarketSnapshot data, FilterSet filters, string? skill, string? company, MarketSettings settings)
    {
        skill = FilterParser.Skill(skill, settings.Catalog);
        var companyKey = string.IsNullOrWhiteSpace(company) ? null : CompanyName.Key(company);
        var postings = filters.Apply(data)
            .Where(p => string.IsNullOrEmpty(skill) || p.Skills.Contains(skill))
            .Where(p => companyKey is null || CompanyName.Key(p.Company) == companyKey)
            .ToList();
        var salaried = postings.Where(p => p.Salary is not null).ToList();

        return new SalaryView(postings.Count, salaried.Count, Analyzer.SalaryCells(salaried), Histogram(salaried),
            [.. salaried.OrderByDescending(p => p.Salary!.YearlyMidpoint).Take(MaxExamples).Select(p => PostingQueries.ToRow(p, settings.Profile))]);
    }

    private static IReadOnlyList<Bucket> Histogram(IReadOnlyList<AnalysisPosting> salaried)
    {
        if (salaried.Count == 0) return [];
        var mids = salaried.Select(p => p.Salary!.YearlyMidpoint).ToList();
        var start = Math.Floor(mids.Min() / BucketWidth) * BucketWidth;
        var end = Math.Floor(mids.Max() / BucketWidth) * BucketWidth + BucketWidth;
        var buckets = new List<Bucket>();
        for (var from = start; from < end; from += BucketWidth)
            buckets.Add(new Bucket(from, from + BucketWidth, mids.Count(m => m >= from && m < from + BucketWidth)));
        return buckets;
    }
}
