using System.Globalization;

namespace Archiver.Storage;

public sealed class RunFolder
{
    public const string DryRunId = "_dryrun";

    private RunFolder(string dataRoot, string runId)
    {
        DataRoot = dataRoot;
        RunId = runId;
        FullPath = Path.Combine(dataRoot, "raw", runId);
    }

    public string DataRoot { get; }
    public string RunId { get; }
    public string FullPath { get; }

    // Real runs are named by local start minute; a second run in the same minute gets seconds appended.
    // Dry runs always reuse data/raw/_dryrun and clear it first so stale files never mislead.
    public static RunFolder Create(string dataRoot, DateTimeOffset startedLocal, bool dryRun)
    {
        if (dryRun)
        {
            var dry = new RunFolder(dataRoot, DryRunId);
            if (Directory.Exists(dry.FullPath)) Directory.Delete(dry.FullPath, recursive: true);
            Directory.CreateDirectory(dry.FullPath);
            return dry;
        }

        var folder = new RunFolder(dataRoot, startedLocal.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture));
        if (Directory.Exists(folder.FullPath))
            folder = new RunFolder(dataRoot, startedLocal.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder.FullPath);
        return folder;
    }

    public string AdzunaSearchFile(string term, string scope, int page) =>
        Path.Combine(FullPath, "adzuna", "search", $"{Slug.From(term)}-{scope}-p{page}.json");

    public string AdzunaAggregateFile(string endpoint, string term) =>
        Path.Combine(FullPath, "adzuna", endpoint, $"{Slug.From(term)}-gta.json");

    public string BoardFile(string ats, string slug) =>
        Path.Combine(FullPath, "boards", $"{ats}-{Slug.From(slug)}.json");

    // One posting's detail, for boards whose list carries no description (SmartRecruiters): a folder named like the list file.
    public string BoardDetailFile(string ats, string slug, string id) =>
        Path.Combine(FullPath, "boards", $"{ats}-{Slug.From(slug)}", $"{Slug.From(id)}.json");

    public string JobBankCatalogFile() => Path.Combine(FullPath, "jobbank", "package_show.json");

    public string ManifestFile() => Path.Combine(FullPath, "manifest.json");

    // Path relative to the data root with forward slashes, as stored in the manifest.
    public string Relative(string fullPath) => Path.GetRelativePath(DataRoot, fullPath).Replace('\\', '/');
}
