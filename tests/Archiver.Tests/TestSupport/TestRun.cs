using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Storage;

namespace Archiver.Tests.TestSupport;

// A run folder + manifest + context on a temp data root. Pass dataRoot to simulate a later run on the same disk.
public sealed class TestRun : IDisposable
{
    private static int _minute;
    private readonly bool _ownsRoot;

    public TestRun(bool dryRun = false, string? dataRoot = null)
    {
        _ownsRoot = dataRoot is null;
        DataRoot = dataRoot ?? Path.Combine(Path.GetTempPath(), "archiver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataRoot);
        var start = new DateTimeOffset(2026, 9, 29, 7, 0, 0, TimeSpan.Zero).AddMinutes(Interlocked.Increment(ref _minute));
        Folder = RunFolder.Create(DataRoot, start, dryRun);
        Manifest = new ManifestBuilder(Folder.RunId, dryRun, "test", start, adzunaBudget: 80, region: "toronto");
        Context = new RunContext(Folder, Manifest, dryRun);
    }

    public string DataRoot { get; }
    public RunFolder Folder { get; }
    public ManifestBuilder Manifest { get; }
    public RunContext Context { get; }

    public IReadOnlyList<RequestRecord> Requests => Manifest.Build(DateTimeOffset.UnixEpoch).Requests;

    public void Dispose()
    {
        if (!_ownsRoot) return;
        try { Directory.Delete(DataRoot, recursive: true); } catch (IOException) { }
    }
}
