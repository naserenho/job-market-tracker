using System.Net;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace Archiver.Tests.Sources;

public class AdzunaSourceTests
{
    private static SourcesOptions Config(int maxCalls = 80) => new()
    {
        Adzuna = new AdzunaOptions
        {
            AppId = "ID1", AppKey = "KEY1", Country = "ca", Where = "Toronto", DistanceKm = 50,
            Terms = ["dotnet developer", "tech lead"],
            MaxPages = 3, MaxCallsPerRun = maxCalls, MinDelayMs = 0,
        },
    };

    // Search pages return `perPage(page)` results; aggregates return a small object.
    private static StubHandler Adzuna(Func<int, int> perPage) => new(req =>
    {
        var path = req.RequestUri!.AbsolutePath;
        if (!path.Contains("/search/")) return StubHandler.Json("{\"histogram\":{}}");
        var n = perPage(int.Parse(path[(path.LastIndexOf('/') + 1)..]));
        return StubHandler.Json($"{{\"count\":999,\"results\":[{string.Join(',', Enumerable.Repeat("{}", n))}]}}");
    });

    private static AdzunaSource Source(StubHandler handler, SourcesOptions config) =>
        new(new StubHttpClientFactory(handler), Options.Create(config));

    [Fact]
    public async Task Stops_paging_a_query_after_a_short_page()
    {
        using var run = new TestRun();
        var handler = Adzuna(page => page == 1 ? 50 : 10);

        await Source(handler, Config()).RunAsync(run.Context, CancellationToken.None);

        // 4 aggregates + 4 (term, scope) queries x 2 pages each
        Assert.Equal(12, handler.Requests.Count);
        Assert.True(File.Exists(run.Folder.AdzunaSearchFile("dotnet developer", "gta", 2)));
        Assert.False(File.Exists(run.Folder.AdzunaSearchFile("dotnet developer", "gta", 3)));
        Assert.Equal(12, run.Manifest.AdzunaCalls);
    }

    [Fact]
    public async Task Budget_caps_calls_and_keeps_aggregates()
    {
        using var run = new TestRun();
        var handler = Adzuna(_ => 50);

        var result = await Source(handler, Config(maxCalls: 5)).RunAsync(run.Context, CancellationToken.None);

        Assert.Equal(5, handler.Requests.Count);
        Assert.All(handler.Requests.Take(2), u => Assert.EndsWith("/histogram", u.AbsolutePath));
        Assert.All(handler.Requests.Skip(2).Take(2), u => Assert.EndsWith("/top_companies", u.AbsolutePath));
        Assert.Contains("budget", result.Note);
    }

    [Fact]
    public async Task Skips_without_calling_when_credentials_are_missing()
    {
        using var run = new TestRun();
        var handler = Adzuna(_ => 50);
        var config = Config();
        config.Adzuna.AppKey = null;

        var result = await Source(handler, config).RunAsync(run.Context, CancellationToken.None);

        Assert.True(result.Skipped);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Dry_run_makes_exactly_one_search_call()
    {
        using var run = new TestRun(dryRun: true);
        var handler = Adzuna(_ => 50);

        await Source(handler, Config()).RunAsync(run.Context, CancellationToken.None);

        var only = Assert.Single(handler.Requests);
        Assert.EndsWith("/search/1", only.AbsolutePath);
    }

    [Fact]
    public async Task Rejected_key_fails_every_query_once_and_never_stores_the_key()
    {
        using var run = new TestRun();
        var handler = new StubHandler(_ => StubHandler.Json("{\"exception\":\"AUTH_FAIL\"}", HttpStatusCode.Unauthorized));

        await Source(handler, Config()).RunAsync(run.Context, CancellationToken.None);

        // 4 aggregates + page 1 only for each of the 4 (term, scope) queries
        Assert.Equal(8, handler.Requests.Count);
        Assert.Equal(SourceOutcome.Failed, run.Manifest.CompleteSource("adzuna").Outcome);
        Assert.All(run.Requests, r => Assert.DoesNotContain("KEY1", r.Url));
    }

    [Theory]
    [InlineData("{\"results\":[{},{}]}", 2)]
    [InlineData("{\"count\":0}", 0)]
    [InlineData("not json", 0)]
    public void CountResults_reads_the_results_array(string body, int expected) =>
        Assert.Equal(expected, AdzunaSource.CountResults(System.Text.Encoding.UTF8.GetBytes(body)));

    // Abu Dhabi: Adzuna has no UAE market, so its region file has no Adzuna section.
    [Fact]
    public async Task A_region_without_adzuna_skips_it_without_a_request()
    {
        using var run = new TestRun();
        var handler = Adzuna(_ => 50);
        var config = Config();
        config.Adzuna.Country = "";

        var result = await Source(handler, config).RunAsync(run.Context, CancellationToken.None);

        Assert.True(result.NotApplicable);
        Assert.Contains("region", result.Note);
        Assert.Empty(handler.Requests);
    }
}
