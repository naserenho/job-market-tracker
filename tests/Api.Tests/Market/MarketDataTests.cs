using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Api.Market;
using Api.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests.Market;

public class MarketDataTests
{
    private readonly ScriptedSource _source = new();
    private readonly ManualClock _clock = new();
    private readonly IMeterFactory _meters = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    private MarketData Create() => new(_source, _clock, _meters, NullLogger<MarketData>.Instance);

    [Fact]
    public async Task First_request_loads_and_requests_within_a_minute_reuse_it()
    {
        var market = Create();
        _source.ThenLoad("v1", 2);

        var first = await market.GetAsync(default);
        _clock.Now += TimeSpan.FromSeconds(59);
        var second = await market.GetAsync(default);

        Assert.Same(first.Data, second.Data);
        Assert.Equal((1, false), (_source.Calls, second.Stale));
    }

    [Fact]
    public async Task After_a_minute_an_unchanged_database_keeps_the_same_data()
    {
        var market = Create();
        _source.ThenLoad("v1", 2).Then(known =>
        {
            Assert.Equal("v1", known);
            return Task.FromResult<MarketLoad?>(null);
        });

        var first = await market.GetAsync(default);
        _clock.Now += MarketData.CheckInterval;
        var second = await market.GetAsync(default);

        Assert.Same(first.Data, second.Data);
        Assert.Equal(2, _source.Calls);
    }

    [Fact]
    public async Task A_new_version_replaces_the_data()
    {
        var market = Create();
        _source.ThenLoad("v1", 2).ThenLoad("v2", 5);

        await market.GetAsync(default);
        _clock.Now += MarketData.CheckInterval;

        Assert.Equal(5, (await market.GetAsync(default)).Data.Postings.Count);
    }

    [Fact]
    public async Task Cold_start_with_the_database_down_throws_and_the_next_request_retries()
    {
        var market = Create();
        _source.ThenDown().ThenLoad("v1", 1);

        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => market.GetAsync(default));
        Assert.Single((await market.GetAsync(default)).Data.Postings);
    }

    [Fact]
    public async Task Losing_the_database_serves_the_last_data_as_stale_until_it_returns()
    {
        var market = Create();
        _source.ThenLoad("v1", 2).ThenDown().ThenUnchanged();

        var first = await market.GetAsync(default);
        _clock.Now += MarketData.CheckInterval;
        var during = await market.GetAsync(default);
        _clock.Now += TimeSpan.FromSeconds(30);
        var stillStale = await market.GetAsync(default);
        _clock.Now += TimeSpan.FromSeconds(30);
        var after = await market.GetAsync(default);

        Assert.Same(first.Data, during.Data);
        Assert.Equal((true, true, false), (during.Stale, stillStale.Stale, after.Stale));
        Assert.Equal(3, _source.Calls);
    }

    [Fact]
    public async Task Concurrent_cold_requests_share_one_load()
    {
        var market = Create();
        var release = new TaskCompletionSource();
        _source.Then(async _ =>
        {
            await release.Task;
            return ScriptedSource.Load("v1", 3);
        });

        var requests = Enumerable.Range(0, 10).Select(_ => market.GetAsync(default)).ToList();
        release.SetResult();
        var reads = await Task.WhenAll(requests);

        Assert.Equal(1, _source.Calls);
        Assert.All(reads, r => Assert.Same(reads[0].Data, r.Data));
    }

    [Fact]
    public async Task Requests_during_a_slow_reload_get_the_current_data_without_waiting()
    {
        var market = Create();
        var release = new TaskCompletionSource();
        _source.ThenLoad("v1", 2).Then(async _ =>
        {
            await release.Task;
            return ScriptedSource.Load("v2", 7);
        });
        var first = await market.GetAsync(default);
        _clock.Now += MarketData.CheckInterval;

        var reloading = market.GetAsync(default);
        var meanwhile = await market.GetAsync(default).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(first.Data, meanwhile.Data);
        Assert.False(reloading.IsCompleted);
        release.SetResult();
        Assert.Equal(7, (await reloading).Data.Postings.Count);
    }

    [Fact]
    public async Task Loads_are_traced_with_counts_and_counted_once_per_new_version()
    {
        long reloads = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Scope == _meters && instrument.Name == "tracker.market_data.reloads") listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref reloads, value));
        var spans = new ConcurrentBag<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == MarketData.TelemetryName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(activityListener);
        var market = Create();
        meterListener.Start();
        _source.ThenLoad("v1", 3).ThenUnchanged();

        await market.GetAsync(default);
        _clock.Now += MarketData.CheckInterval;
        await market.GetAsync(default);

        Assert.Equal(1, Interlocked.Read(ref reloads));
        Assert.Contains(spans, s => s.OperationName == "LoadMarketData"
            && s.GetTagItem("tracker.postings") is 3 && s.GetTagItem("tracker.runs") is 1);
    }

    [Fact]
    public async Task A_reload_that_fails_for_another_reason_keeps_the_data_and_waits_a_minute()
    {
        var market = Create();
        _source.ThenLoad("v1", 2).Then(_ => Task.FromException<MarketLoad?>(new IOException("inbox file busy"))).ThenUnchanged();

        var first = await market.GetAsync(default);
        _clock.Now += MarketData.CheckInterval;
        var failed = await market.GetAsync(default);
        _clock.Now += TimeSpan.FromSeconds(1);
        var soonAfter = await market.GetAsync(default);

        Assert.Same(first.Data, failed.Data);
        Assert.Equal((true, true, 2), (failed.Stale, soonAfter.Stale, _source.Calls));
        _clock.Now += MarketData.CheckInterval;
        Assert.False((await market.GetAsync(default)).Stale);
    }

    // With nothing loaded yet there is nothing to serve as stale: the request fails and the next one tries again.
    [Fact]
    public async Task A_cold_start_that_fails_for_another_reason_still_fails_the_request()
    {
        var market = Create();
        _source.Then(_ => Task.FromException<MarketLoad?>(new IOException("inbox file busy"))).ThenLoad("v1", 1);

        await Assert.ThrowsAsync<IOException>(() => market.GetAsync(default));
        Assert.Single((await market.GetAsync(default)).Data.Postings);
    }

    [Fact]
    public async Task A_caller_that_goes_away_does_not_cancel_the_shared_load()
    {
        var market = Create();
        var release = new TaskCompletionSource();
        _source.Then(async _ =>
        {
            await release.Task;
            return ScriptedSource.Load("v1", 3);
        });
        using var caller = new CancellationTokenSource();

        var request = market.GetAsync(caller.Token);
        caller.Cancel();

        Assert.False(_source.LastToken.IsCancellationRequested);
        release.SetResult();
        Assert.Equal(3, (await request).Data.Postings.Count);
    }
}
