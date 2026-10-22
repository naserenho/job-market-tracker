using Api.Market;
using Microsoft.AspNetCore.Http;
using Tracker.Analysis;
using Tracker.Interpreting;

namespace Api.Queries;

public enum Where { All, Local, Remote }

public enum TextKind { Full, Snippet, All }

public enum PostingSort { Newest, Match, Salary }

public sealed class FilterException(string parameter, string message) : Exception(message)
{
    public string Parameter { get; } = parameter;
}

public sealed record FilterSet(IReadOnlySet<RoleFamily> Families, IReadOnlySet<Seniority>? Seniorities, Where Where, int? WindowDays)
{
    // Postings seen within the window up to the latest run (all history when WindowDays is null).
    public IReadOnlyList<AnalysisPosting> Apply(MarketSnapshot data) =>
        data.LatestRunAt is { } latest
            ? ApplyBetween(data, WindowDays is { } days ? latest.AddDays(-days) : DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
            : [];

    public IReadOnlyList<AnalysisPosting> ApplyBetween(MarketSnapshot data, DateTimeOffset from, DateTimeOffset toExclusive) =>
        [.. data.Postings.Where(p =>
            Families.Contains(p.Family)
            && (Seniorities is null || Seniorities.Contains(p.Seniority))
            && Where switch { Where.Local => p.IsLocal, Where.Remote => p.IsRemoteInCountry, _ => true }
            && p.Sightings.Any(s => s >= from && s < toExclusive))];
}

public static class FilterParser
{
    private static readonly string[] Windows = ["30", "60", "90", "all"];

    public static FilterSet Parse(IQueryCollection query, Profile profile)
    {
        var families = List(query, "families").Select(v => ParseFamily(v)).ToHashSet();
        var seniorities = List(query, "seniority").Select(ParseSeniority).ToHashSet();
        return new FilterSet(
            families.Count > 0 ? families : profile.TargetFamilies,
            seniorities.Count > 0 ? seniorities : null,
            ParseWhere(query),
            Window(query));
    }

    public static TextKind Text(IQueryCollection query, TextKind @default) => ParseEnum(query, "text", @default);

    // A skill filter must name a skill in config/skills.json.
    public static string? Skill(string? skill, SkillCatalog catalog) =>
        string.IsNullOrEmpty(skill) || catalog.Skills.Any(s => s.Name == skill)
            ? skill
            : throw new FilterException("skill", $"Unknown skill '{skill}'.");

    public static PostingSort Sort(IQueryCollection query) => ParseEnum(query, "sort", PostingSort.Newest);

    public static int Int(IQueryCollection query, string name, int @default, int min, int max)
    {
        var raw = query[name].ToString();
        if (raw.Length == 0) return @default;
        if (int.TryParse(raw, out var value) && value >= min && value <= max) return value;
        throw new FilterException(name, $"'{raw}' is not a valid {name}. Use a whole number from {min} to {max}.");
    }

    private static IEnumerable<string> List(IQueryCollection query, string name) =>
        query[name].ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static RoleFamily ParseFamily(string value) =>
        TitleRules.ParseFamily(value.ToLowerInvariant())
        ?? (Enum.TryParse<RoleFamily>(value, ignoreCase: true, out var family) && Enum.IsDefined(family) && !int.TryParse(value, out _)
            ? family
            : throw new FilterException("families", $"Unknown role family '{value}'. Use {string.Join(", ", Enum.GetNames<RoleFamily>())}."));

    private static Seniority ParseSeniority(string value) =>
        Enum.TryParse<Seniority>(value, ignoreCase: true, out var seniority) && Enum.IsDefined(seniority) && !int.TryParse(value, out _)
            ? seniority
            : throw new FilterException("seniority", $"Unknown seniority '{value}'. Use {string.Join(", ", Enum.GetNames<Seniority>())}.");

    private static T ParseEnum<T>(IQueryCollection query, string name, T @default) where T : struct, Enum
    {
        var raw = query[name].ToString();
        if (raw.Length == 0) return @default;
        // Enum.TryParse reads "all,gta" as a bitwise OR of both names, so a comma is refused first.
        return !raw.Contains(',') && Enum.TryParse<T>(raw, ignoreCase: true, out var value) && Enum.IsDefined(value) && !int.TryParse(raw, out _)
            ? value
            : throw new FilterException(name, $"Unknown {name} '{raw}'. Use {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}.");
    }

    // "gta" is what links said before regions existed.
    private static Where ParseWhere(IQueryCollection query) =>
        query["where"].ToString().Equals("gta", StringComparison.OrdinalIgnoreCase) ? Where.Local : ParseEnum(query, "where", Where.All);

    private static int? Window(IQueryCollection query)
    {
        var raw = query["window"].ToString();
        if (raw.Length == 0) return 30;
        if (!Windows.Contains(raw)) throw new FilterException("window", $"Unknown window '{raw}'. Use 30, 60, 90 or all.");
        return raw == "all" ? null : int.Parse(raw);
    }
}
