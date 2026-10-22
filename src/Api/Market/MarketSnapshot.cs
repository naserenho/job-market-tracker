using Tracker.Analysis;
using Tracker.Reading;

namespace Api.Market;

// Everything the endpoints answer from: in-scope, non-duplicate postings plus runs, Job Bank rows and data warnings.
public sealed record MarketSnapshot(
    IReadOnlyList<AnalysisPosting> Postings,
    IReadOnlyList<RunInfo> Runs,
    IReadOnlyList<JobBankMonth> JobBank,
    IReadOnlyList<string> Warnings,
    DateTimeOffset LoadedAt)
{
    public static MarketSnapshot Empty(DateTimeOffset now) => new([], [], [], [], now);

    public DateTimeOffset? LatestRunAt => Runs.Count == 0 ? null : Runs.Max(r => r.StartedAt);
}
