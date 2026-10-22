using System.Text.Json;
using Tracker.Interpreting;

namespace Tracker.Analysis;

public enum SkillLevel { None, Learning, Working, Strong }

// config/profile.json: the owner's target roles and self-assessed skill levels.
public sealed class Profile(IReadOnlySet<RoleFamily> targetFamilies, IReadOnlyDictionary<string, SkillLevel> skills)
{
    public IReadOnlySet<RoleFamily> TargetFamilies { get; } = targetFamilies;
    public IReadOnlyDictionary<string, SkillLevel> Skills { get; } = skills;

    public SkillLevel LevelOf(string skill) => Skills.TryGetValue(skill, out var level) ? level : SkillLevel.None;

    public static double Weight(SkillLevel level) => level switch
    {
        SkillLevel.Strong => 1.0,
        SkillLevel.Working => 0.6,
        SkillLevel.Learning => 0.3,
        _ => 0.0,
    };

    public static Profile Load(string path, SkillCatalog catalog)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var problems = new List<string>();

        var families = new HashSet<RoleFamily>();
        if (root.TryGetProperty("targetFamilies", out var familyList) && familyList.ValueKind == JsonValueKind.Array)
            foreach (var f in familyList.EnumerateArray())
            {
                if (TitleRules.ParseFamily(f.ValueKind == JsonValueKind.String ? f.GetString()! : "") is { } family) families.Add(family);
                else problems.Add($"unknown target family '{f}' (use dotnet, fullstack, software_engineer, lead_manager or other)");
            }
        if (families.Count == 0 && problems.Count == 0) problems.Add("targetFamilies must list at least one role family");

        var known = catalog.Skills.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var levels = new Dictionary<string, SkillLevel>(StringComparer.Ordinal);
        if (root.TryGetProperty("skills", out var skillMap) && skillMap.ValueKind == JsonValueKind.Object)
            foreach (var entry in skillMap.EnumerateObject())
            {
                if (!known.Contains(entry.Name))
                {
                    problems.Add($"skill '{entry.Name}' is not in config/skills.json");
                    continue;
                }
                var text = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
                if (!Enum.TryParse<SkillLevel>(text, ignoreCase: true, out var level) || !Enum.IsDefined(level) || int.TryParse(text, out _))
                {
                    problems.Add($"skill '{entry.Name}' has level '{entry.Value}' (use strong, working, learning or none)");
                    continue;
                }
                levels[entry.Name] = level;
            }

        if (problems.Count > 0) throw new InvalidDataException($"{path}: {string.Join("; ", problems)}.");
        return new Profile(families, levels);
    }
}
