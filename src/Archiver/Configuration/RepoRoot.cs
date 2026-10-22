namespace Archiver.Configuration;

public static class RepoRoot
{
    // Walks up from startDirectory to the first folder containing config/sources.json.
    public static string? Find(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "config", "sources.json")))
                return dir.FullName;
        return null;
    }
}
