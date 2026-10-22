using Archiver.Storage;

namespace Archiver.Configuration;

public static class SourcesValidator
{
    public static readonly IReadOnlyList<string> KnownAts = ["greenhouse", "lever", "ashby", "workable", "smartrecruiters"];

    public static IReadOnlyList<string> Validate(SourcesOptions options)
    {
        var errors = new List<string>();
        var a = options.Adzuna;
        var regionFile = $"config/regions/{options.Region}.json";

        if (string.IsNullOrWhiteSpace(options.Region)) errors.Add("Region is required in config/sources.json.");
        if (a.Enabled) AdzunaRules(a, errors);

        foreach (var b in options.Boards)
        {
            if (string.IsNullOrWhiteSpace(b.Slug)) errors.Add($"A '{b.Ats}' board entry has an empty slug.");
            if (!KnownAts.Contains(b.Ats)) errors.Add($"Board '{b.Slug}' has unknown ats '{b.Ats}' (expected {string.Join(", ", KnownAts.SkipLast(1))} or {KnownAts[^1]}).");
        }
        if (options.Boards.Count == 0)
            errors.Add($"Boards must list at least one job board (check the 'Boards' key in {regionFile}).");
        foreach (var dup in options.Boards.GroupBy(b => (b.Ats, Slug: Slug.From(b.Slug))).Where(g => g.Key.Slug.Length > 0 && g.Count() > 1))
            errors.Add($"Boards {string.Join(", ", dup.Select(b => $"{b.Ats}/{b.Slug}"))} would write the same file '{dup.Key.Ats}-{dup.Key.Slug}.json'.");

        // Job Bank is Canadian: a region without a JobBank section skips it.
        if (!string.IsNullOrWhiteSpace(options.JobBank.DatasetId) && options.JobBank.BackfillMonths < 1)
            errors.Add("JobBank:BackfillMonths must be at least 1.");

        return errors;
    }

    // Only when the region uses Adzuna.
    private static void AdzunaRules(AdzunaOptions a, List<string> errors)
    {
        if (a.Terms.Count == 0) errors.Add("Adzuna:Terms must list at least one search term.");
        foreach (var term in a.Terms.Where(t => Slug.From(t).Length == 0))
            errors.Add($"Adzuna term '{term}' has no letters or digits.");
        foreach (var dup in a.Terms.GroupBy(Slug.From).Where(g => g.Key.Length > 0 && g.Count() > 1))
            errors.Add($"Adzuna terms {string.Join(", ", dup.Select(t => $"'{t}'"))} would share the file name '{dup.Key}'.");
        if (a.ResultsPerPage is < 1 or > 50) errors.Add("Adzuna:ResultsPerPage must be between 1 and 50.");
        if (a.MaxPages < 1) errors.Add("Adzuna:MaxPages must be at least 1.");
        if (a.MaxCallsPerRun is < 1 or > 250) errors.Add("Adzuna:MaxCallsPerRun must be between 1 and 250 (Adzuna's free daily limit).");
        if (a.MinDelayMs < 2400) errors.Add("Adzuna:MinDelayMs must be at least 2400 (Adzuna allows 25 calls per minute).");
        if (string.IsNullOrWhiteSpace(a.Where)) errors.Add("Adzuna:Where is required in the region file.");
        if (a.DistanceKm < 0) errors.Add("Adzuna:DistanceKm must be at least 1 when set.");
    }
}
