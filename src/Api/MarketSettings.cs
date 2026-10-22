using Tracker.Analysis;
using Tracker.Configuration;
using Tracker.Interpreting;

namespace Api;

// Everything the API reads from the repo at start-up: settings, the active region, the skill list and the owner's profile.
// regionName overrides config/sources.json's "Region" (tests, or trying another region without editing the file).
public sealed record MarketSettings(string RepoRoot, string DataRoot, ProcessorOptions Options, Region Region, SkillCatalog Catalog, Profile Profile)
{
    public static MarketSettings Load(string repoRoot, string? profilePath = null, string? dataRoot = null, string? regionName = null)
    {
        var options = ProcessorOptions.Load(Path.Combine(repoRoot, "config", "processor.json"));
        var region = Regions.Load(repoRoot, regionName);
        var catalog = SkillCatalog.Load(Path.Combine(repoRoot, "config", "skills.json"));
        var profileFile = profilePath ?? Path.Combine(repoRoot, "config", "profile.json");
        if (!File.Exists(profileFile))
            throw new InvalidDataException("config/profile.json is missing. Copy config/profile.example.json to config/profile.json and set your skill levels.");
        return new MarketSettings(repoRoot, dataRoot ?? Path.Combine(repoRoot, "data"), options, region, catalog, Profile.Load(profileFile, catalog));
    }
}
