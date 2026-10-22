using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tracker.Interpreting;

public sealed record SkillDefinition(string Name, string Category, IReadOnlyList<string> Aliases);

public sealed class SkillCatalog
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly (string Name, Regex Pattern)[] _matchers;

    public SkillCatalog(IReadOnlyList<SkillDefinition> skills)
    {
        Skills = skills;
        _matchers = [.. skills.Select(s => (s.Name, BuildPattern(s)))];
    }

    public IReadOnlyList<SkillDefinition> Skills { get; }

    public IReadOnlySet<string> Match(string text)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, pattern) in _matchers)
            if (pattern.IsMatch(text)) found.Add(name);
        return found;
    }

    public static SkillCatalog Load(string path)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"{path} is empty.");
        var duplicates = file.Skills.GroupBy(s => s.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0) throw new InvalidDataException($"{path}: duplicate skill names: {string.Join(", ", duplicates)}.");
        var empty = file.Skills.Where(s => s.Aliases.Count == 0).Select(s => s.Name).ToList();
        if (empty.Count > 0) throw new InvalidDataException($"{path}: skills without aliases: {string.Join(", ", empty)}.");
        return new SkillCatalog(file.Skills);
    }

    // Plain aliases: case-insensitive, and no letter, digit, '+' or '#' may touch them, so "C#", ".NET" and "CI/CD" work
    // while "Java" does not match inside "JavaScript". "re:" aliases are case-sensitive regexes for ordinary English words.
    private static Regex BuildPattern(SkillDefinition skill)
    {
        var parts = skill.Aliases.Select(a => a.StartsWith("re:", StringComparison.Ordinal)
            ? $"(?-i:{a[3..]})"
            : $@"(?<![\p{{L}}\p{{N}}+#]){Regex.Escape(a)}(?![\p{{L}}\p{{N}}+#])");
        try
        {
            return new Regex(string.Join('|', parts), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"Skill '{skill.Name}' has an invalid alias pattern: {ex.Message}", ex);
        }
    }

    private sealed record CatalogFile(List<SkillDefinition> Skills);
}
