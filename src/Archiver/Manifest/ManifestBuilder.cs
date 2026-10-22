namespace Archiver.Manifest;

// Collects everything a run did. Thread-safe: board downloads record in parallel.
public sealed class ManifestBuilder(string runId, bool dryRun, string toolVersion, DateTimeOffset startedAt, int adzunaBudget, string region)
{
    private readonly Lock _gate = new();
    private readonly List<RequestRecord> _requests = [];
    private readonly Dictionary<string, SourceSummary> _sources = [];
    private int _adzunaCalls;

    public int InboxFiles { get; set; }
    public int AdzunaCalls => Volatile.Read(ref _adzunaCalls);

    public void Record(RequestRecord record)
    {
        var safe = record with
        {
            Url = UrlRedactor.Redact(record.Url),
            Error = record.Error is null ? null : UrlRedactor.ScrubText(record.Error),
        };
        lock (_gate) _requests.Add(safe);
    }

    public void CountAdzunaCall() => Interlocked.Increment(ref _adzunaCalls);

    public SourceSummary CompleteSource(string source, string? note = null)
    {
        lock (_gate)
        {
            var (ok, failed) = Count(source);
            return _sources[source] = new SourceSummary(Outcomes.From(ok, failed), ok, failed, note);
        }
    }

    public SourceSummary SkipSource(string source, string note)
    {
        lock (_gate) return _sources[source] = new SourceSummary(SourceOutcome.Skipped, 0, 0, note);
    }

    public SourceSummary NotApplicable(string source, string note)
    {
        lock (_gate) return _sources[source] = new SourceSummary(SourceOutcome.NotApplicable, 0, 0, note);
    }

    // The source threw; whatever it saved before that still counts, so earlier successes make it partial.
    public SourceSummary FailSource(string source, string error)
    {
        lock (_gate)
        {
            var (ok, failed) = Count(source);
            var outcome = ok > 0 ? SourceOutcome.Partial : SourceOutcome.Failed;
            return _sources[source] = new SourceSummary(outcome, ok, failed, UrlRedactor.ScrubText(error));
        }
    }

    // The source finished but judged its own responses unusable, so it is failed whatever its requests returned.
    public SourceSummary MarkFailed(string source, string note)
    {
        lock (_gate)
        {
            var (ok, failed) = Count(source);
            return _sources[source] = new SourceSummary(SourceOutcome.Failed, ok, failed, UrlRedactor.ScrubText(note));
        }
    }

    public RunManifest Build(DateTimeOffset finishedAt, bool cancelled = false)
    {
        lock (_gate)
        {
            return new RunManifest(runId, region, dryRun, toolVersion, startedAt, finishedAt,
                new Dictionary<string, SourceSummary>(_sources), AdzunaCalls, adzunaBudget, InboxFiles, [.. _requests], cancelled);
        }
    }

    private (int Ok, int Failed) Count(string source)
    {
        var ok = _requests.Count(r => r.Source == source && r.Error is null);
        var failed = _requests.Count(r => r.Source == source && r.Error is not null);
        return (ok, failed);
    }
}
