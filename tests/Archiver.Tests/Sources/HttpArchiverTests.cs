using System.IO.Compression;
using System.Net;
using Archiver.Sources;
using Archiver.Tests.TestSupport;

namespace Archiver.Tests.Sources;

public class HttpArchiverTests
{
    [Fact]
    public async Task GetAndSave_writes_body_verbatim_and_records_redacted_url()
    {
        using var run = new TestRun();
        var http = new HttpClient(new StubHandler(_ => StubHandler.Json("{\"a\": 1}")));
        var file = Path.Combine(run.Folder.FullPath, "x.json");

        var result = await HttpArchiver.GetAndSaveAsync(http, "adzuna", "https://a.test/s?app_id=I&app_key=K&what=x", file, run.Context, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("{\"a\": 1}", File.ReadAllText(file));
        var record = Assert.Single(run.Requests);
        Assert.Equal("https://a.test/s?what=x", record.Url);
        Assert.Equal(200, record.Status);
        Assert.Equal(run.Folder.Relative(file), record.File);
        Assert.Null(record.Error);
    }

    [Fact]
    public async Task GetAndSave_does_not_save_error_responses()
    {
        using var run = new TestRun();
        var http = new HttpClient(new StubHandler(_ => StubHandler.Json("{}", HttpStatusCode.ServiceUnavailable)));
        var file = Path.Combine(run.Folder.FullPath, "x.json");

        var result = await HttpArchiver.GetAndSaveAsync(http, "boards", "https://a.test/b", file, run.Context, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.False(File.Exists(file));
        Assert.Equal("HTTP 503", Assert.Single(run.Requests).Error);
    }

    [Fact]
    public async Task GetAndSave_records_exceptions_without_leaking_credentials()
    {
        using var run = new TestRun();
        var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("failed for https://a.test/?app_key=SECRET")));

        var result = await HttpArchiver.GetAndSaveAsync(http, "adzuna", "https://a.test/?app_key=SECRET", Path.Combine(run.Folder.FullPath, "x.json"), run.Context, CancellationToken.None);

        Assert.False(result.Ok);
        var record = Assert.Single(run.Requests);
        Assert.DoesNotContain("SECRET", record.Error);
        Assert.DoesNotContain("SECRET", record.Url);
    }

    [Fact]
    public async Task DownloadGzip_round_trips_the_exact_bytes()
    {
        using var run = new TestRun();
        var original = new byte[] { 0xFF, 0xFE, 0x57, 0x00, 0x09, 0x00, 0x41, 0x00 }; // UTF-16 BOM + "W\tA"
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(original) }));
        var file = Path.Combine(run.DataRoot, "jobbank", "2026-08_x.csv.gz");

        var ok = await HttpArchiver.DownloadGzipAsync(http, "jobbank", "https://files.test/x.csv", file, run.Context, CancellationToken.None);

        Assert.True(ok);
        await using var gz = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
        using var copy = new MemoryStream();
        await gz.CopyToAsync(copy);
        Assert.Equal(original, copy.ToArray());
        Assert.Equal(original.Length, Assert.Single(run.Requests).Bytes);
    }

    [Fact]
    public async Task DownloadGzip_gives_up_on_a_stalled_stream_and_leaves_nothing_behind()
    {
        using var run = new TestRun();
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(new byte[1000])),
        }));
        var file = Path.Combine(run.DataRoot, "jobbank", "2026-08_x.csv.gz");
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // the run token never fires in production

        var ok = await HttpArchiver.DownloadGzipAsync(http, "jobbank", "https://files.test/x.csv", file, run.Context, safety.Token, idleTimeout: TimeSpan.FromMilliseconds(200));

        Assert.False(safety.IsCancellationRequested);
        Assert.False(ok);
        Assert.False(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"));
        Assert.Contains("No data received", Assert.Single(run.Requests).Error);
    }

    [Fact]
    public async Task DownloadGzip_leaves_nothing_behind_when_the_stream_breaks()
    {
        using var run = new TestRun();
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingStream(new byte[100_000])),
        }));
        var file = Path.Combine(run.DataRoot, "jobbank", "2026-08_x.csv.gz");

        var ok = await HttpArchiver.DownloadGzipAsync(http, "jobbank", "https://files.test/x.csv", file, run.Context, CancellationToken.None);

        Assert.False(ok);
        Assert.False(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"));
        Assert.Contains("Connection reset", Assert.Single(run.Requests).Error);
    }
}
