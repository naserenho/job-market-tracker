using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Api.Market;

public sealed record MarketRead(MarketSnapshot Data, bool Stale);

// Keeps the analysed market in memory. At most once a minute a request checks the database version and swaps in a
// fresh copy when it changed. Only a cold start waits for a load; later requests never queue behind a reload.
// A load serves every request, so it runs until the application stops, not until the request that started it goes away.
public sealed class MarketData : IDisposable
{
    public const string TelemetryName = "Tracker.MarketData";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);
    private static readonly ActivitySource Activities = new(TelemetryName);

    private readonly IMarketSource _source;
    private readonly TimeProvider _clock;
    private readonly ILogger<MarketData> _log;
    private readonly Counter<long> _reloads;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationToken _stopping;
    private volatile State? _state;

    private sealed record State(MarketSnapshot Data, string Version, DateTimeOffset CheckedAt, bool Stale);

    public MarketData(IMarketSource source, TimeProvider clock, IMeterFactory meters, ILogger<MarketData> log,
        IHostApplicationLifetime? lifetime = null)
    {
        _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
        _source = source;
        _clock = clock;
        _log = log;
        _reloads = meters.Create(TelemetryName).CreateCounter<long>("tracker.market_data.reloads",
            description: "Times the market data was loaded from the database.");
    }

    public async Task<MarketRead> GetAsync(CancellationToken ct)
    {
        var state = _state;
        if (state is not null && !Due(state)) return Read(state);

        if (state is null) await _gate.WaitAsync(ct);
        else if (!await _gate.WaitAsync(0, ct)) return Read(state); // another request is already refreshing

        try
        {
            state = _state;
            if (state is null || Due(state)) _state = state = await RefreshAsync(state, _stopping);
            return Read(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static MarketRead Read(State state) => new(state.Data, state.Stale);

    private bool Due(State state) => _clock.GetUtcNow() - state.CheckedAt >= CheckInterval;

    private async Task<State> RefreshAsync(State? current, CancellationToken ct)
    {
        using var activity = Activities.StartActivity("LoadMarketData");
        try
        {
            var load = await _source.LoadIfChangedAsync(current?.Version, ct);
            activity?.SetTag("tracker.changed", load is not null);
            if (load is null)
                return (current ?? throw new InvalidOperationException("The market source returned nothing for a first load."))
                    with { CheckedAt = _clock.GetUtcNow(), Stale = false };

            _reloads.Add(1);
            activity?.SetTag("tracker.runs", load.Data.Runs.Count);
            activity?.SetTag("tracker.postings", load.Data.Postings.Count);
            _log.LogInformation("Market data loaded: {Runs} runs, {Postings} postings", load.Data.Runs.Count, load.Data.Postings.Count);
            return new State(load.Data, load.Version, _clock.GetUtcNow(), Stale: false);
        }
        catch (DatabaseUnavailableException ex) when (current is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _log.LogWarning(ex, "Tracker database unavailable; serving the data loaded at {LoadedAt}", current.Data.LoadedAt);
            return current with { CheckedAt = _clock.GetUtcNow(), Stale = true };
        }
        catch (Exception ex) when (current is not null && ex is not OperationCanceledException)
        {
            // Not a database outage (an inbox file being saved, say). Keep serving and try again after the normal
            // interval, not on every request.
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _log.LogError(ex, "Market data reload failed; serving the data loaded at {LoadedAt}", current.Data.LoadedAt);
            return current with { CheckedAt = _clock.GetUtcNow(), Stale = true };
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
