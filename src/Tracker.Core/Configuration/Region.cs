using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Tracker.Configuration;

// One job market: everything that differs between, say, Toronto and Abu Dhabi (ADR 0015). config/sources.json names
// the active one; its file is config/regions/<name>.json. List entries described as "patterns" are regular-expression
// fragments matched as whole words: plain words work as written; escape a literal dot as \.
public sealed class Region
{
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string LocalLabel { get; set; } = "";
    public string RemoteLabel { get; set; } = "";
    public string CountryCode { get; set; } = "";
    [JsonRequired]
    public List<string> CountryNames { get; set; } = [];
    public string Culture { get; set; } = "";
    public string TimeZone { get; set; } = "";
    [JsonRequired]
    public RegionMarkers InCountry { get; set; } = new();
    [JsonRequired]
    public RegionMarkers Foreign { get; set; } = new();
    [JsonRequired]
    public RegionLocal Local { get; set; } = new();
    [JsonRequired]
    public RegionSalary Salary { get; set; } = new();
    [JsonRequired]
    public List<string> CitizenshipPatterns { get; set; } = [];
    public RegionAdzuna? Adzuna { get; set; }
    [JsonRequired]
    public List<RegionBoard> Boards { get; set; } = [];
    public Dictionary<string, string> CompanyNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public JobBankOptions? JobBank { get; set; }

    [JsonIgnore]
    public CultureInfo CultureInfo => CultureInfo.GetCultureInfo(Culture);

    // Board files only carry a slug; prefer the configured display name, else capitalize the slug.
    public string CompanyName(string slug) =>
        CompanyNames.TryGetValue(slug, out var name) ? name : slug.Length == 0 ? slug : char.ToUpperInvariant(slug[0]) + slug[1..];
}

public sealed class RegionMarkers
{
    // Patterns, case-insensitive.
    [JsonRequired]
    public List<string> Words { get; set; } = [];

    // Literal and case-sensitive, after "," (in-country codes also after "-"): province or state codes.
    [JsonRequired]
    public List<string> Codes { get; set; } = [];
}

public sealed class RegionLocal
{
    // Place name as written in postings → place shown in reports.
    [JsonRequired]
    public Dictionary<string, string> Places { get; set; } = [];

    // Names shared with places in other countries: they count only with in-country context in the same segment.
    [JsonRequired]
    public List<string> NeedsCountryContext { get; set; } = [];

    // Optional, for a local area too large to list (a province): a segment with one of these words or codes is local
    // even when it names no listed place.
    public RegionMarkers? Anywhere { get; set; }

    // Optional patterns that rule a segment out of Anywhere unless the structured country is this one ("Ontario, CA" is
    // also a city in California).
    public List<string>? Exclude { get; set; }

    // Optional: the place shown → the area it is grouped under in the report's "Where the jobs are".
    public Dictionary<string, string>? Areas { get; set; }
}

public sealed class RegionSalary
{
    [JsonRequired]
    public string Currency { get; set; } = "";
    [JsonRequired]
    public string MoneyPrefix { get; set; } = "";

    // Literal marks before an amount (besides the currency code itself), and short marks after one.
    [JsonRequired]
    public List<string> Signs { get; set; } = [];
    [JsonRequired]
    public List<string> ShortMarks { get; set; } = [];

    // The same for currencies that rule a range out.
    [JsonRequired]
    public List<string> ForeignCurrencies { get; set; } = [];
    [JsonRequired]
    public List<string> ForeignSigns { get; set; } = [];
    [JsonRequired]
    public List<string> ForeignShortMarks { get; set; } = [];

    // What a plain "$" means: "ambiguous" (context decides) or "foreign".
    [JsonRequired]
    public string BareDollar { get; set; } = "ambiguous";

    // Ranges written as "25,000 - 35,000 AED", with the code after the amounts only.
    [JsonRequired]
    public bool CodeAfterAmount { get; set; }

    // A per-month range is a salary (paid monthly), not an allowance.
    [JsonRequired]
    public bool MonthlyIsSalary { get; set; }

    // Patterns near a range that confirm it or rule it out, and literal case-sensitive codes that rule it out.
    [JsonRequired]
    public List<string> ContextWords { get; set; } = [];
    [JsonRequired]
    public List<string> ForeignContextWords { get; set; } = [];
    [JsonRequired]
    public List<string> ForeignContextCodes { get; set; } = [];

    // Plausible yearly salary in the home currency: outside it a range is a parse error or not a salary.
    [JsonRequired]
    public decimal YearlyMin { get; set; }
    [JsonRequired]
    public decimal YearlyMax { get; set; }
}

public sealed class RegionAdzuna
{
    public string Country { get; set; } = "";
    public string Where { get; set; } = "";
    public int DistanceKm { get; set; }
}

public sealed class RegionBoard
{
    public string Ats { get; set; } = "";
    public string Slug { get; set; } = "";
}

public static class Regions
{
    // Collections and pasted files from before regions existed were all Toronto.
    public const string Legacy = "toronto";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string ActiveName(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "config", "sources.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.TryGetProperty("Region", out var name) && name.ValueKind == JsonValueKind.String && name.GetString()!.Trim().Length > 0
            ? name.GetString()!.Trim()
            : throw new InvalidDataException($"{path}: add \"Region\": \"<name>\" naming a file in config/regions/ (for example \"toronto\").");
    }

    public static Region Load(string repoRoot, string? name = null)
    {
        name = string.IsNullOrWhiteSpace(name) ? ActiveName(repoRoot) : name.Trim();
        var folder = Path.Combine(repoRoot, "config", "regions");
        var path = Path.Combine(folder, $"{name}.json");
        if (!File.Exists(path))
        {
            var known = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.Ordinal)
                : Enumerable.Empty<string>();
            throw new InvalidDataException($"Region '{name}' has no file {path}. Available: {string.Join(", ", known)}.");
        }
        return LoadFile(path);
    }

    public static Region LoadFile(string path)
    {
        Region region;
        try
        {
            region = JsonSerializer.Deserialize<Region>(File.ReadAllText(path), Json) ?? throw new InvalidDataException($"{path} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }
        region.CompanyNames = new Dictionary<string, string>(region.CompanyNames, StringComparer.OrdinalIgnoreCase);
        var problems = Validate(region);
        // The archiver tags collections with the file name and the processor filters by Name: they must agree.
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (region.Name.Length > 0 && !region.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            problems.Add($"Name '{region.Name}' must match the file name '{fileName}'.");
        if (problems.Count > 0) throw new InvalidDataException($"{path}: {string.Join(" ", problems)}");
        return region;
    }

    private static List<string> Validate(Region r)
    {
        var problems = new List<string>();
        void Required(string value, string member)
        {
            if (string.IsNullOrWhiteSpace(value)) problems.Add($"{member} is required.");
        }

        Required(r.Name, "Name");
        Required(r.Title, "Title");
        Required(r.LocalLabel, "LocalLabel");
        Required(r.RemoteLabel, "RemoteLabel");
        Required(r.CountryCode, "CountryCode");
        Required(r.Culture, "Culture");
        Required(r.TimeZone, "TimeZone");
        Required(r.Salary.Currency, "Salary.Currency");
        Required(r.Salary.MoneyPrefix, "Salary.MoneyPrefix");
        if (r.Local.Places.Count == 0) problems.Add("Local.Places must list at least one place.");
        if (r.Boards.Count == 0) problems.Add("Boards must list at least one job board.");
        if (r.Salary.YearlyMin <= 0 || r.Salary.YearlyMin >= r.Salary.YearlyMax)
            problems.Add("Salary.YearlyMin must be above 0 and below Salary.YearlyMax.");
        if (r.Salary.BareDollar is not ("ambiguous" or "foreign")) problems.Add("Salary.BareDollar must be \"ambiguous\" or \"foreign\".");
        if (r.Culture.Length > 0)
        {
            try { _ = CultureInfo.GetCultureInfo(r.Culture, predefinedOnly: true); }
            catch (CultureNotFoundException) { problems.Add($"Culture '{r.Culture}' is not a known culture."); }
        }
        if (r.TimeZone.Length > 0)
        {
            // Browsers (the dashboard) only know IANA names such as "Asia/Dubai", not Windows ids.
            try
            {
                if (!TimeZoneInfo.FindSystemTimeZoneById(r.TimeZone).HasIanaId)
                    problems.Add($"TimeZone '{r.TimeZone}' must be an IANA name such as \"America/Toronto\".");
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { problems.Add($"TimeZone '{r.TimeZone}' is not a known time zone."); }
        }
        // An empty entry matches everywhere: every location would look foreign, every range like home pay.
        foreach (var (list, member) in new[]
        {
            (r.CountryNames, "CountryNames"), (r.InCountry.Words, "InCountry.Words"), (r.InCountry.Codes, "InCountry.Codes"),
            (r.Foreign.Words, "Foreign.Words"), (r.Foreign.Codes, "Foreign.Codes"), (r.Local.NeedsCountryContext, "Local.NeedsCountryContext"),
            (r.Salary.Signs, "Salary.Signs"), (r.Salary.ShortMarks, "Salary.ShortMarks"), (r.Salary.ForeignCurrencies, "Salary.ForeignCurrencies"),
            (r.Salary.ForeignSigns, "Salary.ForeignSigns"), (r.Salary.ForeignShortMarks, "Salary.ForeignShortMarks"),
            (r.Salary.ContextWords, "Salary.ContextWords"), (r.Salary.ForeignContextWords, "Salary.ForeignContextWords"),
            (r.Salary.ForeignContextCodes, "Salary.ForeignContextCodes"), (r.CitizenshipPatterns, "CitizenshipPatterns"),
            (r.Local.Anywhere?.Words ?? [], "Local.Anywhere.Words"), (r.Local.Anywhere?.Codes ?? [], "Local.Anywhere.Codes"),
            (r.Local.Exclude ?? [], "Local.Exclude"),
        })
            if (list.Any(string.IsNullOrWhiteSpace)) problems.Add($"{member} has an empty entry.");
        if (r.Local.Places.Any(p => string.IsNullOrWhiteSpace(p.Key) || string.IsNullOrWhiteSpace(p.Value)))
            problems.Add("Local.Places has an empty name or place.");
        if (r.Local.Areas?.Any(a => string.IsNullOrWhiteSpace(a.Key) || string.IsNullOrWhiteSpace(a.Value)) == true)
            problems.Add("Local.Areas has an empty place or area.");
        var places = new HashSet<string>(r.Local.Places.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var name in r.Local.NeedsCountryContext.Where(n => !string.IsNullOrWhiteSpace(n) && !places.Contains(n)))
            problems.Add($"Local.NeedsCountryContext: '{name}' is not one of Local.Places.");
        if (r.Adzuna is { } adzuna)
        {
            Required(adzuna.Country, "Adzuna.Country");
            Required(adzuna.Where, "Adzuna.Where");
            // Left out (0), no distance is sent: a province-wide search.
            if (adzuna.DistanceKm < 0) problems.Add("Adzuna.DistanceKm must be at least 1 when set.");
        }
        if (r.JobBank is { } jobBank && string.IsNullOrWhiteSpace(jobBank.EconomicRegion) == string.IsNullOrWhiteSpace(jobBank.Province))
            problems.Add("JobBank: set exactly one of EconomicRegion or Province.");

        foreach (var (list, member) in new[]
        {
            (r.CitizenshipPatterns, "CitizenshipPatterns"), (r.InCountry.Words, "InCountry.Words"), (r.Foreign.Words, "Foreign.Words"),
            (r.Salary.ContextWords, "Salary.ContextWords"), (r.Salary.ForeignContextWords, "Salary.ForeignContextWords"),
            (r.Local.Anywhere?.Words ?? [], "Local.Anywhere.Words"), (r.Local.Exclude ?? [], "Local.Exclude"),
        })
            foreach (var pattern in list)
            {
                try { _ = new Regex(pattern); }
                catch (ArgumentException) { problems.Add($"{member}: '{pattern}' is not a valid pattern."); }
            }
        return problems;
    }
}
