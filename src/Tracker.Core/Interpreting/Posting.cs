using System.Text.RegularExpressions;
using Tracker.Configuration;
using Tracker.Reading;

namespace Tracker.Interpreting;

// One unique job after interpretation. Id = "<source>:<sourceId>"; SightingRuns are in chronological order.
public sealed record Posting(
    string Id,
    string Source,
    string SourceId,
    string Company,
    string Title,
    string Url,
    string LocationText,
    Scope Scope,
    RoleFamily Family,
    Seniority Seniority,
    SalaryRange? Salary,
    string SalaryOrigin,
    string Description,
    bool IsFullText,
    bool CitizenshipOrClearance,
    IReadOnlySet<string> Skills,
    IReadOnlyList<string> SightingRuns,
    string? DuplicateOf)
{
    public string FirstSeenRun => SightingRuns[0];
    public string LastSeenRun => SightingRuns[^1];
}

// Scope, salary and the citizenship flag follow the region's rules; title and skills are the same everywhere.
public sealed class Interpreter(Region region, SkillCatalog skills)
{
    private readonly ScopeRules _scope = new(region);
    private readonly SalaryParser _salary = new(region.Salary);
    private readonly Regex _citizenship = new(RegionPatterns.Words(region.CitizenshipPatterns), RegexOptions.IgnoreCase);

    public Posting Interpret(RawPosting raw, IReadOnlyList<string> sightingRuns)
    {
        var where = _scope.Classify(raw);
        var inCountry = where.InScope || _scope.IsInCountry(raw);

        var salary = raw.Salary is { } structured && structured.IsPlausibleWithin(region.Salary.YearlyMin, region.Salary.YearlyMax) ? structured : null;
        var origin = "structured";
        if (salary is null)
        {
            salary = _salary.FromText(raw.Description, inCountry);
            origin = salary is null ? "none" : "parsed";
        }

        var text = $"{raw.Title}\n{raw.Description}";
        var skillText = SkillText.ForMatching(raw.Title, raw.Description);
        return new Posting(raw.Key, raw.Source, raw.SourceId, raw.Company, raw.Title, raw.Url, raw.Location, where,
            TitleRules.Family(raw.Title), TitleRules.Seniority(raw.Title), salary, origin, raw.Description, raw.IsFullText,
            _citizenship.IsMatch(text), skills.Match(skillText), sightingRuns, DuplicateOf: null);
    }
}

// Keeps the latest content per posting plus every run that saw it. Runs must be added in chronological order.
public sealed class SightingCollector
{
    private readonly Dictionary<string, (RawPosting Latest, List<string> Runs)> _byKey = new(StringComparer.Ordinal);

    public void Add(RawPosting raw)
    {
        if (_byKey.TryGetValue(raw.Key, out var entry))
        {
            if (entry.Runs[^1] != raw.RunId) entry.Runs.Add(raw.RunId);
            _byKey[raw.Key] = (raw, entry.Runs);
        }
        else
        {
            _byKey[raw.Key] = (raw, [raw.RunId]);
        }
    }

    public IEnumerable<(RawPosting Latest, IReadOnlyList<string> Runs)> Entries =>
        _byKey.Values.Select(e => (e.Latest, (IReadOnlyList<string>)e.Runs));
}
