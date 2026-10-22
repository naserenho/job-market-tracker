using Tracker.Configuration;

namespace Tracker.TestSupport;

// Loads the committed config/ files so tests pin their real content.
public static class RepoConfig
{
    public static string Root { get; } = RepoRoot.Find(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Repo root not found.");
    public static string PathOf(string file) => Path.Combine(Root, "config", file);
    public static ProcessorOptions Options() => ProcessorOptions.Load(PathOf("processor.json"));
    public static Region Toronto { get; } = Regions.LoadFile(PathOf("regions/toronto.json"));
    public static Region AbuDhabi { get; } = Regions.LoadFile(PathOf("regions/abu-dhabi.json"));
    public static Region Ontario { get; } = Regions.LoadFile(PathOf("regions/ontario.json"));
}
