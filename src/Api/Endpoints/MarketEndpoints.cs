using Api.Market;
using Api.Queries;
using Tracker.Analysis;

namespace Api.Endpoints;

public sealed record OverviewView(ReportModel? Report);

public static class MarketEndpoints
{
    public static void MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/meta", async (HttpContext http, MarketData market, MarketSettings settings) =>
        {
            var read = await ReadAsync(http, market);
            return MetaQueries.Meta(read.Data, settings, read.Stale);
        });

        api.MapGet("/overview", async (HttpContext http, MarketData market, MarketSettings settings, TimeProvider clock) =>
        {
            var filters = FilterParser.Parse(http.Request.Query, settings.Profile);
            var read = await ReadAsync(http, market);
            return new OverviewView(MetaQueries.Overview(read.Data, filters, settings, clock.GetLocalNow()));
        });

        api.MapGet("/skills", async (HttpContext http, MarketData market, MarketSettings settings, string? category) =>
        {
            var filters = FilterParser.Parse(http.Request.Query, settings.Profile);
            var text = FilterParser.Text(http.Request.Query, TextKind.Full);
            var read = await ReadAsync(http, market);
            return SkillQueries.List(read.Data, filters, text, category, settings);
        });

        // Catch-all so names with "/" (CI/CD) match; %2F stays encoded in the path, so unescape once more.
        api.MapGet("/skills/{**name}", async (HttpContext http, MarketData market, MarketSettings settings, string name) =>
        {
            var skill = Uri.UnescapeDataString(name);
            var filters = FilterParser.Parse(http.Request.Query, settings.Profile);
            var text = FilterParser.Text(http.Request.Query, TextKind.Full);
            var read = await ReadAsync(http, market);
            return SkillQueries.Detail(read.Data, filters, text, skill, settings) is { } detail
                ? Results.Ok(detail)
                : Results.Problem(statusCode: 404, title: "Unknown skill", detail: $"'{skill}' is not in config/skills.json.");
        });

        api.MapGet("/salaries", async (HttpContext http, MarketData market, MarketSettings settings, string? skill, string? company) =>
        {
            var filters = FilterParser.Parse(http.Request.Query, settings.Profile);
            var read = await ReadAsync(http, market);
            return SalaryQueries.View(read.Data, filters, skill, company, settings);
        });

        api.MapGet("/postings", async (HttpContext http, MarketData market, MarketSettings settings, string? q, string? skill, string? company) =>
        {
            var query = http.Request.Query;
            var filters = FilterParser.Parse(query, settings.Profile);
            var sort = FilterParser.Sort(query);
            var page = FilterParser.Int(query, "page", 1, 1, 10_000);
            var pageSize = FilterParser.Int(query, "pageSize", 25, 1, 100);
            skill = FilterParser.Skill(skill, settings.Catalog);
            var read = await ReadAsync(http, market);
            return PostingQueries.Page(read.Data, filters, q, skill, company, sort, page, pageSize, settings.Profile);
        });
    }

    // Called after the filters are parsed, so a bad filter is a 400 even while the database is down.
    private static async Task<MarketRead> ReadAsync(HttpContext http, MarketData market)
    {
        var read = await market.GetAsync(http.RequestAborted);
        if (read.Stale) http.Response.Headers["X-Data-Stale"] = "true";
        return read;
    }
}
