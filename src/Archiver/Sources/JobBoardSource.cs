using System.Text.Json;
using System.Text.Json.Nodes;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Storage;
using Microsoft.Extensions.Options;

namespace Archiver.Sources;

public sealed record BoardCheck(BoardConfig Board, int? Status, int? Jobs, string? Error)
{
    public bool Ok => Error is null;
}

public sealed class JobBoardSource(IHttpClientFactory httpFactory, IOptions<SourcesOptions> options) : IArchiveSource
{
    public const string SourceName = "boards";
    public const string HttpClientName = "boards";
    public const int MaxParallel = 4;
    public const int SmartRecruitersMaxPages = 10;

    public string Name => SourceName;

    public async Task<SourceResult> RunAsync(RunContext ctx, CancellationToken ct)
    {
        var boards = options.Value.Boards;
        if (boards.Count == 0) return SourceResult.Skip("No boards configured.");

        var selected = ctx.DryRun ? boards.Take(1).ToList() : boards;
        var http = httpFactory.CreateClient(HttpClientName);
        var country = options.Value.CountryCode;
        await Parallel.ForEachAsync(selected, new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = ct },
            async (board, token) =>
            {
                if (board.Ats == "smartrecruiters") await SmartRecruitersAsync(http, board, country, ctx, token);
                else await HttpArchiver.GetAndSaveAsync(http, SourceName, BoardUrls.For(board), ctx.Folder.BoardFile(board.Ats, board.Slug), ctx, token);
            });
        return SourceResult.Ran();
    }

    // SmartRecruiters lists 100 postings a page, already filtered to the country, and without descriptions. The pages are
    // saved as one list file, then each in-country posting's detail into a folder beside it. A dry run keeps to page 1.
    // Like GetAndSaveAsync for the other boards, a problem here fails this board only: it is recorded and the run goes on.
    private static async Task SmartRecruitersAsync(HttpClient http, BoardConfig board, string country, RunContext ctx, CancellationToken ct)
    {
        var listUrl = BoardUrls.For(board, country);
        try
        {
            await SmartRecruitersBoardAsync(http, board, country, listUrl, ctx, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            ctx.Manifest.Record(new RequestRecord(SourceName, listUrl, null, 0, 0, null, $"Unreadable SmartRecruiters answer: {ex.Message}"));
        }
    }

    private static async Task SmartRecruitersBoardAsync(HttpClient http, BoardConfig board, string country, string listUrl, RunContext ctx, CancellationToken ct)
    {
        var file = ctx.Folder.BoardFile(board.Ats, board.Slug);
        var content = new JsonArray();
        var total = 0;
        var pagesRead = 0;
        for (var page = 0; page < (ctx.DryRun ? 1 : SmartRecruitersMaxPages); page++)
        {
            var url = page == 0 ? listUrl : $"{listUrl}&offset={page * BoardUrls.SmartRecruitersPageSize}";
            var result = await HttpArchiver.GetAsync(http, SourceName, url, ctx.Folder.Relative(file), ctx, ct);
            if (!result.Ok) break;
            if (JsonNode.Parse(result.Body) is not JsonObject { } root || root["content"] is not JsonArray items)
                throw new InvalidDataException("the list is not an object with a \"content\" array");
            pagesRead++;
            if (page == 0) total = root["totalFound"] is JsonValue found && found.TryGetValue<int>(out var n)
                ? n
                : throw new InvalidDataException("\"totalFound\" is not a number");
            foreach (var item in items) content.Add(item?.DeepClone());
            if (items.Count == 0 || content.Count >= total) break;
        }
        // Like the other boards: no file when the board never answered with a list.
        if (pagesRead == 0) return;
        var merged = new JsonObject { ["offset"] = 0, ["limit"] = BoardUrls.SmartRecruitersPageSize, ["totalFound"] = total, ["content"] = content };
        await AtomicFile.WriteAsync(file, JsonSerializer.SerializeToUtf8Bytes(merged), ct);
        if (ctx.DryRun) return;

        foreach (var posting in content.OfType<JsonObject>())
        {
            var id = Text(posting["id"]);
            var postingCountry = Text(posting["location"] is JsonObject location ? location["country"] : null);
            if (id is null || !string.Equals(postingCountry, country, StringComparison.OrdinalIgnoreCase)) continue;
            await HttpArchiver.GetAndSaveAsync(http, SourceName, BoardUrls.SmartRecruitersDetail(board, id), ctx.Folder.BoardDetailFile(board.Ats, board.Slug, id), ctx, ct);
        }
    }

    // A JSON string as written, a number as its digits ("id": 5), anything else null.
    private static string? Text(JsonNode? node) =>
        node is JsonValue value ? value.TryGetValue<string>(out var text) ? text : value.ToJsonString() : null;

    // Checks every configured board without saving anything; used by --verify-boards.
    public async Task<IReadOnlyList<BoardCheck>> VerifyAsync(CancellationToken ct)
    {
        var boards = options.Value.Boards;
        var http = httpFactory.CreateClient(HttpClientName);
        var results = new BoardCheck[boards.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, boards.Count), new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = ct },
            async (i, token) => results[i] = await CheckAsync(http, boards[i], options.Value.CountryCode, token));
        return results;
    }

    private static async Task<BoardCheck> CheckAsync(HttpClient http, BoardConfig board, string country, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(BoardUrls.For(board, country), ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode) return new BoardCheck(board, status, null, $"HTTP {status}");
            var jobs = BoardUrls.CountJobs(board.Ats, await response.Content.ReadAsByteArrayAsync(ct));
            return new BoardCheck(board, status, jobs, jobs is null ? "Unexpected response shape" : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new BoardCheck(board, null, null, ex.Message);
        }
    }
}
