using System.Net;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace Archiver.Tests.Sources;

public class JobBoardSourceTests
{
    private static SourcesOptions Config(params (string Ats, string Slug)[] boards) => new()
    {
        Boards = [.. boards.Select(b => new BoardConfig { Ats = b.Ats, Slug = b.Slug })],
    };

    private static JobBoardSource Source(StubHandler handler, SourcesOptions config) =>
        new(new StubHttpClientFactory(handler), Options.Create(config));

    [Fact]
    public async Task Saves_every_board_and_keeps_going_after_a_failure()
    {
        using var run = new TestRun();
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath.Contains("gone")
            ? StubHandler.Json("{}", HttpStatusCode.NotFound)
            : StubHandler.Json("{\"jobs\":[]}"));

        await Source(handler, Config(("greenhouse", "faire"), ("greenhouse", "gone"), ("ashby", "cohere"))).RunAsync(run.Context, CancellationToken.None);

        Assert.True(File.Exists(run.Folder.BoardFile("greenhouse", "faire")));
        Assert.True(File.Exists(run.Folder.BoardFile("ashby", "cohere")));
        Assert.False(File.Exists(run.Folder.BoardFile("greenhouse", "gone")));
        Assert.Equal(SourceOutcome.Partial, run.Manifest.CompleteSource("boards").Outcome);
    }

    [Fact]
    public async Task Never_runs_more_than_four_requests_at_once()
    {
        using var run = new TestRun();
        int current = 0, peak = 0;
        var handler = new StubHandler(async (_, ct) =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref peak, now);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref current);
            return StubHandler.Json("{\"jobs\":[]}");
        });
        var boards = Enumerable.Range(0, 12).Select(i => ("greenhouse", $"co{i}")).ToArray();

        await Source(handler, Config(boards)).RunAsync(run.Context, CancellationToken.None);

        Assert.Equal(12, handler.Requests.Count);
        Assert.InRange(peak, 2, JobBoardSource.MaxParallel);
    }

    [Fact]
    public async Task Dry_run_fetches_only_the_first_board()
    {
        using var run = new TestRun(dryRun: true);
        var handler = new StubHandler(_ => StubHandler.Json("{\"jobs\":[]}"));

        await Source(handler, Config(("greenhouse", "faire"), ("lever", "wattpad"))).RunAsync(run.Context, CancellationToken.None);

        Assert.Contains("faire", Assert.Single(handler.Requests).AbsolutePath);
    }

    [Fact]
    public async Task Skips_when_no_boards_are_configured()
    {
        using var run = new TestRun();
        var result = await Source(new StubHandler(_ => StubHandler.Json("{}")), Config()).RunAsync(run.Context, CancellationToken.None);
        Assert.True(result.Skipped);
    }

    [Fact]
    public async Task Verify_reports_job_counts_and_problems_in_config_order()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            var p when p.Contains("faire") => StubHandler.Json("{\"jobs\":[{},{}]}"),
            var p when p.Contains("wattpad") => StubHandler.Json("<html>not json</html>"),
            _ => StubHandler.Json("{}", HttpStatusCode.NotFound),
        });

        var checks = await Source(handler, Config(("greenhouse", "faire"), ("lever", "wattpad"), ("ashby", "gone"))).VerifyAsync(CancellationToken.None);

        Assert.Equal(new[] { "faire", "wattpad", "gone" }, checks.Select(c => c.Board.Slug));
        Assert.True(checks[0].Ok);
        Assert.Equal(2, checks[0].Jobs);
        Assert.False(checks[1].Ok);
        Assert.False(checks[2].Ok);
        Assert.Equal(404, checks[2].Status);
    }

    private static string SrPosting(int id, string country) =>
        "{\"id\":\"" + id + "\",\"name\":\"Job " + id + "\",\"location\":{\"city\":\"X\",\"country\":\"" + country + "\",\"remote\":false}}";

    private static string SrPage(int total, IEnumerable<string> postings) =>
        "{\"offset\":0,\"limit\":100,\"totalFound\":" + total + ",\"content\":[" + string.Join(",", postings) + "]}";

    private static SourcesOptions SmartRecruiters()
    {
        var config = Config(("smartrecruiters", "Acme"));
        config.CountryCode = "CA";
        return config;
    }

    private static bool IsList(Uri u) => u.AbsolutePath.EndsWith("/postings", StringComparison.Ordinal);

    // The list is filtered to the country by SmartRecruiters and paged; only in-country postings get a detail request.
    [Fact]
    public async Task Smartrecruiters_pages_the_list_and_fetches_details_for_in_country_postings()
    {
        using var run = new TestRun();
        var handler = new StubHandler(req =>
        {
            var uri = req.RequestUri!;
            if (!IsList(uri)) return StubHandler.Json("{\"id\":\"d\",\"jobAd\":{\"sections\":{}}}");
            return StubHandler.Json(System.Web.HttpUtility.ParseQueryString(uri.Query)["offset"] is null or "0"
                ? SrPage(150, [SrPosting(1, "us"), .. Enumerable.Range(2, 99).Select(i => SrPosting(i, "ca"))])
                : SrPage(150, Enumerable.Range(101, 50).Select(i => SrPosting(i, "ca"))));
        });

        await Source(handler, SmartRecruiters()).RunAsync(run.Context, CancellationToken.None);

        var lists = handler.Requests.Where(IsList).ToList();
        Assert.Equal(2, lists.Count);
        Assert.All(lists, u => Assert.Contains("country=ca", u.Query));
        using (var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(run.Folder.BoardFile("smartrecruiters", "Acme"))))
            Assert.Equal(150, saved.RootElement.GetProperty("content").GetArrayLength());
        var details = handler.Requests.Where(u => !IsList(u)).ToList();
        Assert.Equal(149, details.Count);
        Assert.DoesNotContain(details, u => u.AbsolutePath.EndsWith("/postings/1", StringComparison.Ordinal));
        Assert.True(File.Exists(run.Folder.BoardDetailFile("smartrecruiters", "Acme", "2")));
        Assert.Equal(SourceOutcome.Succeeded, run.Manifest.CompleteSource("boards").Outcome);
    }

    // A malformed answer fails that board only; the other boards are still saved (final review I4).
    [Theory]
    [InlineData("[]")]
    [InlineData("{\"totalFound\":\"many\",\"content\":[]}")]
    public async Task A_malformed_smartrecruiters_answer_fails_only_that_board(string body)
    {
        using var run = new TestRun();
        var handler = new StubHandler(req => req.RequestUri!.Host.Contains("smartrecruiters")
            ? StubHandler.Json(body)
            : StubHandler.Json("{\"jobs\":[]}"));
        var config = Config(("smartrecruiters", "Acme"), ("greenhouse", "faire"));
        config.CountryCode = "CA";

        await Source(handler, config).RunAsync(run.Context, CancellationToken.None);

        Assert.True(File.Exists(run.Folder.BoardFile("greenhouse", "faire")));
        Assert.Equal(SourceOutcome.Partial, run.Manifest.CompleteSource("boards").Outcome);
    }

    [Fact]
    public async Task A_numeric_smartrecruiters_id_still_gets_its_detail()
    {
        using var run = new TestRun();
        var handler = new StubHandler(req => IsList(req.RequestUri!)
            ? StubHandler.Json("{\"totalFound\":1,\"content\":[{\"id\":5,\"name\":\"Dev\",\"location\":{\"country\":\"ca\"}}]}")
            : StubHandler.Json("{\"id\":5}"));

        await Source(handler, SmartRecruiters()).RunAsync(run.Context, CancellationToken.None);

        Assert.Contains(handler.Requests, u => u.AbsolutePath.EndsWith("/postings/5", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Smartrecruiters_list_stops_after_ten_pages()
    {
        using var run = new TestRun();
        var handler = new StubHandler(_ => StubHandler.Json(SrPage(5000, Enumerable.Range(0, 100).Select(i => SrPosting(i, "us")))));

        await Source(handler, SmartRecruiters()).RunAsync(run.Context, CancellationToken.None);

        Assert.Equal(10, handler.Requests.Count);
    }

    [Fact]
    public async Task Smartrecruiters_dry_run_fetches_the_first_page_and_no_details()
    {
        using var run = new TestRun(dryRun: true);
        var handler = new StubHandler(_ => StubHandler.Json(SrPage(150, Enumerable.Range(0, 100).Select(i => SrPosting(i, "ca")))));

        await Source(handler, SmartRecruiters()).RunAsync(run.Context, CancellationToken.None);

        Assert.True(IsList(Assert.Single(handler.Requests)));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }
}
