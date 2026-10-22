using Microsoft.Extensions.Logging;
using Tracker.Configuration;
using Tracker.Data;
using Tracker.Interpreting;
using Tracker.Reading;

namespace Tracker.Rebuild;

public sealed record RebuildSummary(int Runs, int Postings, int CountedPostings, IReadOnlyList<string> Warnings);

// Reads every archived run oldest-first, interprets and deduplicates postings, and replaces the database content.
public sealed class RebuildService(TrackerDb db, Region region, SkillCatalog catalog, ILogger<RebuildService> log)
{
    public async Task<RebuildSummary> RunAsync(string dataRoot, CancellationToken ct)
    {
        var all = Archive.Runs(dataRoot);
        var runs = ForRegion(all, region);
        if (runs.Count == 0)
            throw new InvalidOperationException($"No completed archive runs for region '{region.Name}' under {Path.Combine(dataRoot, "raw")}. Run the archiver first.");

        var warnings = new List<string>();
        if (OtherRegions(all, region) is { } skipped) warnings.Add(skipped);
        var collector = new SightingCollector();
        var aggregates = new List<AdzunaAggregate>();
        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var raw in Archive.ReadPostings(run, region, warnings)) collector.Add(raw);
            aggregates.AddRange(Archive.ReadAggregates(run, warnings));
        }

        var inbox = InboxReader.ReadAll(Path.Combine(dataRoot, "inbox"), runs, region.Name);
        foreach (var raw in inbox.Postings) collector.Add(raw);

        var interpreter = new Interpreter(region, catalog);
        var postings = Deduplicator.MarkDuplicates(
            [.. collector.Entries.Select(e => interpreter.Interpret(e.Latest, e.Runs))],
            [.. runs.Select(r => r.RunId)]);
        var jobBank = JobBankReader.SummarizeDirectory(Path.Combine(dataRoot, "jobbank"), region.JobBank, warnings);

        await new RebuildWriter(db).WriteAsync(new RebuildData(runs, catalog.Skills, postings, aggregates, jobBank.Months, jobBank.Regions), ct);

        var counted = postings.Count(p => p.Scope.InScope && p.DuplicateOf is null);
        log.LogInformation("Rebuilt from {Runs} runs: {Postings} postings, {Counted} in scope after duplicates, {JobBank} Job Bank rows, {Warnings} warnings",
            runs.Count, postings.Count, counted, jobBank.Months.Count, warnings.Count + inbox.Warnings.Count);
        return new RebuildSummary(runs.Count, postings.Count, counted,
            [.. Archive.CollectionProblems(runs[^1]), .. warnings, .. inbox.Warnings]);
    }

    // The active region's runs, oldest first. Another region's collections never mix into this one's numbers.
    public static IReadOnlyList<ArchiveRun> ForRegion(IReadOnlyList<ArchiveRun> runs, Region region) =>
        [.. runs.Where(r => r.Region.Equals(region.Name, StringComparison.OrdinalIgnoreCase))];

    public static string? OtherRegions(IReadOnlyList<ArchiveRun> runs, Region region)
    {
        var others = runs.Where(r => !r.Region.Equals(region.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (others.Count == 0) return null;
        var names = string.Join(", ", others.Select(r => r.Region).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal));
        return $"Skipped {others.Count} {(others.Count == 1 ? "collection" : "collections")} from other regions ({names}); the active region is {region.Name}.";
    }
}
