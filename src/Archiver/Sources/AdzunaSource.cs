using System.Text.Json;
using Archiver.Configuration;
using Microsoft.Extensions.Options;

namespace Archiver.Sources;

public sealed class AdzunaSource(IHttpClientFactory httpFactory, IOptions<SourcesOptions> options) : IArchiveSource
{
    public const string SourceName = "adzuna";
    public const string HttpClientName = "adzuna";

    public string Name => SourceName;

    public async Task<SourceResult> RunAsync(RunContext ctx, CancellationToken ct)
    {
        var o = options.Value.Adzuna;
        if (!o.Enabled)
            return SourceResult.NotUsedInRegion("Adzuna has no market for this region (no Adzuna section in its region file).");
        if (!o.HasCredentials)
            return SourceResult.Skip("Adzuna AppId/AppKey not configured (see README: dotnet user-secrets).");

        var http = httpFactory.CreateClient(HttpClientName);
        var plan = AdzunaQueryPlan.Build(o);
        if (ctx.DryRun) plan = [.. plan.Where(r => r.Kind == AdzunaKind.Search).Take(1)];

        var exhausted = new HashSet<(string Term, string Scope)>();
        var calls = 0;
        var notMade = 0;
        foreach (var request in plan)
        {
            if (request.Kind == AdzunaKind.Search && exhausted.Contains((request.Term, request.Scope))) continue;
            if (calls >= o.MaxCallsPerRun) { notMade++; continue; }
            if (calls > 0 && o.MinDelayMs > 0) await Task.Delay(o.MinDelayMs, ct);

            calls++;
            ctx.Manifest.CountAdzunaCall();
            var file = request.Kind switch
            {
                AdzunaKind.Search => ctx.Folder.AdzunaSearchFile(request.Term, request.Scope, request.Page),
                AdzunaKind.Histogram => ctx.Folder.AdzunaAggregateFile("histogram", request.Term),
                _ => ctx.Folder.AdzunaAggregateFile("top_companies", request.Term),
            };
            var result = await HttpArchiver.GetAndSaveAsync(http, SourceName, AdzunaQueryPlan.Url(o, request), file, ctx, ct);

            // A failed or short page is the last one for that query.
            if (request.Kind == AdzunaKind.Search && (!result.Ok || CountResults(result.Body) < o.ResultsPerPage))
                exhausted.Add((request.Term, request.Scope));
        }

        return SourceResult.Ran(notMade > 0 ? $"{notMade} planned calls not made: budget of {o.MaxCallsPerRun} reached." : null);
    }

    public static int CountResults(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("results", out var results)
                && results.ValueKind == JsonValueKind.Array
                ? results.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
