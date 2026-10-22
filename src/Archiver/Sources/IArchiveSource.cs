using Archiver.Manifest;
using Archiver.Storage;

namespace Archiver.Sources;

public interface IArchiveSource
{
    string Name { get; }

    // Records every request in ctx.Manifest. Throws only for bugs; expected failures are recorded, not thrown.
    Task<SourceResult> RunAsync(RunContext ctx, CancellationToken ct);
}

public sealed record RunContext(RunFolder Folder, ManifestBuilder Manifest, bool DryRun);

public sealed record SourceResult(bool Skipped, string? Note, bool Failed = false, bool NotApplicable = false)
{
    public static SourceResult Ran(string? note = null) => new(false, note);
    public static SourceResult Skip(string note) => new(true, note);

    // The active region doesn't use this source at all: recorded, but never reported as a collection problem.
    public static SourceResult NotUsedInRegion(string note) => new(true, note, NotApplicable: true);

    // The source got responses but could not use them (e.g. a changed format): failed even if its requests succeeded.
    public static SourceResult Fail(string note) => new(false, note, Failed: true);
}
