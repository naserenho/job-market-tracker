using System.Collections.Concurrent;
using Api.Market;

namespace Api.Tests.TestSupport;

public sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

// A market source that plays back scripted answers, one per call.
public sealed class ScriptedSource : IMarketSource
{
    private readonly ConcurrentQueue<Func<string?, Task<MarketLoad?>>> _steps = new();
    private int _calls;

    public int Calls => _calls;

    // The token the latest load was given.
    public CancellationToken LastToken { get; private set; }

    public ScriptedSource Then(Func<string?, Task<MarketLoad?>> step)
    {
        _steps.Enqueue(step);
        return this;
    }

    public ScriptedSource ThenLoad(string version, int postings) => Then(_ => Task.FromResult<MarketLoad?>(Load(version, postings)));
    public ScriptedSource ThenUnchanged() => Then(_ => Task.FromResult<MarketLoad?>(null));
    public ScriptedSource ThenDown() => Then(_ => Task.FromException<MarketLoad?>(new DatabaseUnavailableException(new TimeoutException())));

    public Task<MarketLoad?> LoadIfChangedAsync(string? knownVersion, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        LastToken = ct;
        return _steps.TryDequeue(out var step) ? step(knownVersion) : throw new InvalidOperationException("No scripted answer left.");
    }

    public static MarketLoad Load(string version, int postings) =>
        new(version, Data.MarketOf([.. Enumerable.Range(0, postings).Select(i => Data.P($"p{i}", ["C#"], [0]))], 0));
}
