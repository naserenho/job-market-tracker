using System.Text.Json;
using Archiver.Configuration;
using Archiver.Manifest;
using Microsoft.Extensions.Options;

namespace Archiver.Sources;

public sealed class JobBankSource(IHttpClientFactory httpFactory, IOptions<SourcesOptions> options) : IArchiveSource
{
    public const string SourceName = "jobbank";
    public const string HttpClientName = "jobbank";

    public string Name => SourceName;

    public async Task<SourceResult> RunAsync(RunContext ctx, CancellationToken ct)
    {
        var o = options.Value.JobBank;
        if (string.IsNullOrWhiteSpace(o.DatasetId)) return SourceResult.NotUsedInRegion("Job Bank is not configured for this region.");
        var http = httpFactory.CreateClient(HttpClientName);
        var catalogUrl = $"{o.CatalogUrl}?id={Uri.EscapeDataString(o.DatasetId)}";

        var catalog = await HttpArchiver.GetAndSaveAsync(http, SourceName, catalogUrl, ctx.Folder.JobBankCatalogFile(), ctx, ct);
        if (!catalog.Ok) return SourceResult.Ran("Catalog request failed; no monthly files checked.");

        IReadOnlyList<JobBankResource> resources;
        try
        {
            resources = JobBankCatalog.ParseEnglishCsvs(catalog.Body);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return SourceResult.Fail($"Unexpected catalog format ({ex.Message}); no monthly files checked.");
        }

        if (ctx.DryRun) return SourceResult.Ran($"Dry run: {resources.Count} English monthly CSVs listed, none downloaded.");

        var dir = Path.Combine(ctx.Folder.DataRoot, "jobbank");
        var todo = JobBankCatalog.SelectToDownload(resources, JobBankCatalog.MonthsOnDisk(dir), o.BackfillMonths);
        foreach (var resource in todo) // one at a time: each file is ~50 MB
            await HttpArchiver.DownloadGzipAsync(http, SourceName, resource.Url, Path.Combine(dir, resource.FileName), ctx, ct);

        return SourceResult.Ran(todo.Count == 0 ? "Up to date; no new monthly files." : $"Attempted {todo.Count} monthly file(s).");
    }
}
