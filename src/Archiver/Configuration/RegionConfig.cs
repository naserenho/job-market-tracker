using Microsoft.Extensions.Configuration;

namespace Archiver.Configuration;

// The active region's file (config/regions/<name>.json) holds what to collect for that market: Adzuna's country and
// place, the job boards and Job Bank. Program.cs layers it over config/sources.json; their section names match.
public static class RegionConfig
{
    public static string Folder(string root) => Path.Combine(root, "config", "regions");

    public static IReadOnlyList<string> Available(string root) =>
        Directory.Exists(Folder(root))
            ? [.. Directory.GetFiles(Folder(root), "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.Ordinal)]
            : [];

    // The region file's path: the region named in config/sources.json, or `region` when given. Null with a message
    // when it is missing or unknown.
    public static string? Resolve(string root, string? region, out string error)
    {
        error = "";
        region ??= new ConfigurationBuilder().AddJsonFile(Path.Combine(root, "config", "sources.json")).Build()["Region"];
        if (string.IsNullOrWhiteSpace(region))
        {
            error = "config/sources.json has no \"Region\". Add \"Region\": \"<name>\" naming a file in config/regions/ (for example \"toronto\").";
            return null;
        }
        var path = Path.Combine(Folder(root), $"{region.Trim()}.json");
        if (File.Exists(path)) return path;
        error = $"Region '{region}' has no file {path}. Available: {string.Join(", ", Available(root))}.";
        return null;
    }
}
