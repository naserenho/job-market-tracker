using System.Diagnostics;
using System.IO.Compression;
using Archiver.Manifest;
using Archiver.Storage;

namespace Archiver.Sources;

public sealed record FetchResult(bool Ok, int? Status, byte[] Body);

// Every outbound request goes through here so each one is saved atomically and lands in the manifest exactly once.
public static class HttpArchiver
{
    public static async Task<FetchResult> GetAndSaveAsync(HttpClient http, string source, string url, string filePath, RunContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync(url, ct);
            var body = await response.Content.ReadAsByteArrayAsync(ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                ctx.Manifest.Record(new RequestRecord(source, url, status, body.Length, sw.ElapsedMilliseconds, null, $"HTTP {status}"));
                return new FetchResult(false, status, body);
            }

            await AtomicFile.WriteAsync(filePath, body, ct);
            ctx.Manifest.Record(new RequestRecord(source, url, status, body.Length, sw.ElapsedMilliseconds, ctx.Folder.Relative(filePath), null));
            return new FetchResult(true, status, body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            ctx.Manifest.Record(new RequestRecord(source, url, null, 0, sw.ElapsedMilliseconds, null, ex.Message));
            return new FetchResult(false, null, []);
        }
    }

    // Fetches without saving, for responses the caller combines into one file (SmartRecruiters' list pages); the
    // manifest names that file.
    public static async Task<FetchResult> GetAsync(HttpClient http, string source, string url, string file, RunContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync(url, ct);
            var body = await response.Content.ReadAsByteArrayAsync(ct);
            var status = (int)response.StatusCode;
            var ok = response.IsSuccessStatusCode;
            ctx.Manifest.Record(new RequestRecord(source, url, status, body.Length, sw.ElapsedMilliseconds, ok ? file : null, ok ? null : $"HTTP {status}"));
            return new FetchResult(ok, status, body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            ctx.Manifest.Record(new RequestRecord(source, url, null, 0, sw.ElapsedMilliseconds, null, ex.Message));
            return new FetchResult(false, null, []);
        }
    }

    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(60);

    // Streams a large body straight into a gzip file without buffering it in memory.
    // A body that stops arriving for idleTimeout (default 60 s) fails the download instead of hanging the run.
    public static async Task<bool> DownloadGzipAsync(HttpClient http, string source, string url, string filePath, RunContext ctx, CancellationToken ct, TimeSpan? idleTimeout = null)
    {
        var sw = Stopwatch.StartNew();
        var idleLimit = idleTimeout ?? DefaultIdleTimeout;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        long bytes = 0;
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                ctx.Manifest.Record(new RequestRecord(source, url, status, 0, sw.ElapsedMilliseconds, null, $"HTTP {status}"));
                return false;
            }

            await AtomicFile.WriteAsync(filePath, async (file, token) =>
            {
                await using var gzip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true);
                await using var body = await response.Content.ReadAsStreamAsync(token);
                var buffer = new byte[81920];
                while (true)
                {
                    idle.CancelAfter(idleLimit);
                    var read = await body.ReadAsync(buffer, idle.Token);
                    if (read == 0) break;
                    await gzip.WriteAsync(buffer.AsMemory(0, read), token);
                    bytes += read;
                }
            }, ct);

            ctx.Manifest.Record(new RequestRecord(source, url, status, bytes, sw.ElapsedMilliseconds, ctx.Folder.Relative(filePath), null));
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && idle.IsCancellationRequested)
        {
            ctx.Manifest.Record(new RequestRecord(source, url, null, bytes, sw.ElapsedMilliseconds, null, $"No data received for {idleLimit.TotalSeconds:0.#} s; download abandoned."));
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            ctx.Manifest.Record(new RequestRecord(source, url, null, bytes, sw.ElapsedMilliseconds, null, ex.Message));
            return false;
        }
    }
}
