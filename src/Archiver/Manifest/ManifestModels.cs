namespace Archiver.Manifest;

// NotApplicable: the region doesn't use this source (Adzuna has no UAE market; Job Bank is Canadian). Not a problem.
public enum SourceOutcome { Succeeded, Partial, Failed, Skipped, NotApplicable }

public sealed record RequestRecord(string Source, string Url, int? Status, long Bytes, long DurationMs, string? File, string? Error);

public sealed record SourceSummary(SourceOutcome Outcome, int Ok, int Failed, string? Note);

public sealed record RunManifest(
    string RunId,
    string Region,
    bool DryRun,
    string ToolVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyDictionary<string, SourceSummary> Sources,
    int AdzunaCalls,
    int AdzunaBudget,
    int InboxFiles,
    IReadOnlyList<RequestRecord> Requests,
    bool Cancelled = false);

public static class ExitCodes
{
    public const int Ok = 0;
    public const int AllFailed = 1;
    public const int ConfigInvalid = 2;
}

public static class Outcomes
{
    // A source that made no successful request collected nothing, so it counts as failed.
    public static SourceOutcome From(int ok, int failed) => (ok, failed) switch
    {
        (0, _) => SourceOutcome.Failed,
        (_, 0) => SourceOutcome.Succeeded,
        _ => SourceOutcome.Partial,
    };

    public static int ExitCode(IEnumerable<SourceOutcome> outcomes) =>
        outcomes.Any(o => o is SourceOutcome.Succeeded or SourceOutcome.Partial) ? ExitCodes.Ok : ExitCodes.AllFailed;
}
