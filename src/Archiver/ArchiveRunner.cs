using System.Reflection;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Archiver;

public sealed class ArchiveRunner(IEnumerable<IArchiveSource> sources, IOptions<SourcesOptions> options, TimeProvider time, ILogger<ArchiveRunner> log)
{
    private static readonly string ToolVersion =
        typeof(ArchiveRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public async Task<int> RunAsync(string dataRoot, bool dryRun, CancellationToken ct)
    {
        var started = time.GetLocalNow();
        var folder = RunFolder.Create(dataRoot, started, dryRun);
        var manifest = new ManifestBuilder(folder.RunId, dryRun, ToolVersion, started, options.Value.Adzuna.MaxCallsPerRun, options.Value.Region)
        {
            InboxFiles = Inbox.EnsureAndCount(dataRoot),
        };
        var ctx = new RunContext(folder, manifest, dryRun);
        log.LogInformation("Run {RunId} started{DryRun}", folder.RunId, dryRun ? " (dry run)" : "");

        // Sources run one after another: keeps Adzuna pacing simple and the log readable.
        var cancelled = false;
        foreach (var source in sources)
        {
            log.LogInformation("Source {Source}: starting", source.Name);
            try
            {
                var result = await source.RunAsync(ctx, ct);
                if (result.NotApplicable) manifest.NotApplicable(source.Name, result.Note ?? "Not used in this region.");
                else if (result.Skipped) manifest.SkipSource(source.Name, result.Note ?? "Skipped.");
                else if (result.Failed) manifest.MarkFailed(source.Name, result.Note ?? "Failed.");
                else manifest.CompleteSource(source.Name, result.Note);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Stop here, but still write the manifest so what was already saved is accounted for.
                manifest.FailSource(source.Name, "Run cancelled.");
                cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Source {Source} crashed", source.Name);
                manifest.FailSource(source.Name, ex.Message);
            }
        }

        var finished = manifest.Build(time.GetLocalNow(), cancelled);
        await ManifestWriter.WriteAsync(finished, folder.ManifestFile(), CancellationToken.None);
        foreach (var (name, s) in finished.Sources)
            log.LogInformation("Source {Source}: {Outcome} ({Ok} ok, {Failed} failed) {Note}", name, s.Outcome, s.Ok, s.Failed, s.Note ?? "");
        log.LogInformation("Manifest: {Path}{Cancelled}", folder.ManifestFile(), cancelled ? " (run cancelled)" : "");

        return cancelled ? ExitCodes.AllFailed : Outcomes.ExitCode(finished.Sources.Values.Select(s => s.Outcome));
    }
}
