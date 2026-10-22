using System.IO.Compression;
using System.Net;
using System.Text;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace Archiver.Tests.Sources;

public class JobBankSourceTests
{
    private static readonly byte[] CatalogJson = JobBankCatalogTests.Catalog(
        ("en-aug", "August 2026 Job Postings", "en", "CSV"),
        ("en-jul", "July 2026 Job Postings", "en", "CSV"),
        ("en-jun", "June 2026 Job Postings", "en", "CSV"),
        ("fr-aug", "August 2026 Job Postings", "fr", "CSV"));

    private static JobBankSource Source(StubHandler handler, int backfill = 2) =>
        new(new StubHttpClientFactory(handler), Options.Create(new SourcesOptions
        {
            JobBank = new JobBankOptions { DatasetId = "ds1", CatalogUrl = "https://catalog.test/package_show", BackfillMonths = backfill },
        }));

    private static StubHandler Healthy() => new(req => req.RequestUri!.Host == "catalog.test"
        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CatalogJson) }
        : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"col\n{req.RequestUri.AbsolutePath}") });

    [Fact]
    public async Task Downloads_newest_months_gzipped_and_saves_the_catalog()
    {
        using var run = new TestRun();
        var handler = Healthy();

        await Source(handler).RunAsync(run.Context, CancellationToken.None);

        var dir = Path.Combine(run.DataRoot, "jobbank");
        Assert.Equal(new[] { "2026-07_en-jul.csv.gz", "2026-08_en-aug.csv.gz" }, Directory.GetFiles(dir).Select(Path.GetFileName).Order());
        Assert.True(File.Exists(run.Folder.JobBankCatalogFile()));
        await using var gz = new GZipStream(File.OpenRead(Path.Combine(dir, "2026-08_en-aug.csv.gz")), CompressionMode.Decompress);
        Assert.Equal("col\n/en-aug.csv", await new StreamReader(gz, Encoding.UTF8).ReadToEndAsync());
        Assert.Contains("ds1", handler.Requests[0].Query);
    }

    [Fact]
    public async Task A_later_run_downloads_nothing_new()
    {
        using var first = new TestRun();
        await Source(Healthy()).RunAsync(first.Context, CancellationToken.None);
        using var second = new TestRun(dataRoot: first.DataRoot);
        var handler = Healthy();

        var result = await Source(handler).RunAsync(second.Context, CancellationToken.None);

        Assert.Single(handler.Requests); // catalog only
        Assert.Contains("Up to date", result.Note);
    }

    [Fact]
    public async Task Dry_run_reads_the_catalog_but_downloads_no_csv()
    {
        using var run = new TestRun(dryRun: true);
        var handler = Healthy();

        await Source(handler).RunAsync(run.Context, CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.False(Directory.Exists(Path.Combine(run.DataRoot, "jobbank")));
    }

    [Fact]
    public async Task Catalog_failure_makes_the_source_fail()
    {
        using var run = new TestRun();
        var handler = new StubHandler(_ => StubHandler.Json("{}", HttpStatusCode.InternalServerError));

        await Source(handler).RunAsync(run.Context, CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.Equal(SourceOutcome.Failed, run.Manifest.CompleteSource("jobbank").Outcome);
    }

    [Fact]
    public async Task Broken_download_is_retried_on_the_next_run()
    {
        using var first = new TestRun();
        var breaking = new StubHandler(req => req.RequestUri!.Host == "catalog.test"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CatalogJson) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream(new byte[1000])) });
        await Source(breaking, backfill: 1).RunAsync(first.Context, CancellationToken.None);

        Assert.Empty(JobBankCatalog.MonthsOnDisk(Path.Combine(first.DataRoot, "jobbank")));
        Assert.Equal(SourceOutcome.Partial, first.Manifest.CompleteSource("jobbank").Outcome);

        using var second = new TestRun(dataRoot: first.DataRoot);
        await Source(Healthy(), backfill: 1).RunAsync(second.Context, CancellationToken.None);
        Assert.Equal(new[] { new DateOnly(2026, 8, 1) }, JobBankCatalog.MonthsOnDisk(Path.Combine(first.DataRoot, "jobbank")));
    }

    [Fact]
    public async Task Unexpected_catalog_shape_fails_the_source_without_throwing()
    {
        using var run = new TestRun();
        var handler = new StubHandler(_ => StubHandler.Json("{\"success\":true}"));

        var result = await Source(handler).RunAsync(run.Context, CancellationToken.None);

        Assert.True(result.Failed);
        Assert.Contains("catalog", result.Note);
    }

    [Fact]
    public async Task A_region_without_job_bank_skips_it_without_a_request()
    {
        using var run = new TestRun();
        var handler = Healthy();
        var source = new JobBankSource(new StubHttpClientFactory(handler), Options.Create(new SourcesOptions()));

        var result = await source.RunAsync(run.Context, CancellationToken.None);

        Assert.True(result.NotApplicable);
        Assert.Contains("region", result.Note);
        Assert.Empty(handler.Requests);
    }
}
