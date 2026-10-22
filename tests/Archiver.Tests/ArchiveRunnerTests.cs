using System.Text.Json;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Archiver.Tests;

public class ArchiveRunnerTests
{
    private sealed class FakeSource(string name, Func<RunContext, Task<SourceResult>> run) : IArchiveSource
    {
        public string Name => name;
        public Task<SourceResult> RunAsync(RunContext ctx, CancellationToken ct) => run(ctx);
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static ArchiveRunner Runner(params IArchiveSource[] sources) =>
        new(sources, Options.Create(new SourcesOptions()), new FixedTime(new DateTimeOffset(2026, 9, 29, 7, 0, 0, TimeSpan.Zero)), NullLogger<ArchiveRunner>.Instance);

    private static FakeSource Succeeds(string name) => new(name, ctx =>
    {
        ctx.Manifest.Record(new RequestRecord(name, "https://a.test/?app_key=SECRET", 200, 10, 1, "raw/x.json", null));
        return Task.FromResult(SourceResult.Ran());
    });

    private static FakeSource Throws(string name) => new(name, _ => throw new InvalidOperationException("kaboom"));

    private static FakeSource Skips(string name) => new(name, _ => Task.FromResult(SourceResult.Skip("not today")));

    [Fact]
    public async Task Writes_manifest_and_returns_0_when_any_source_succeeds()
    {
        using var tmp = new TempDir();

        var code = await Runner(Succeeds("adzuna"), Throws("boards"), Skips("jobbank")).RunAsync(tmp.Path, dryRun: false, CancellationToken.None);

        Assert.Equal(ExitCodes.Ok, code);
        var json = File.ReadAllText(Path.Combine(tmp.Path, "raw", "2026-09-29_0700", "manifest.json"));
        using var doc = JsonDocument.Parse(json);
        var sources = doc.RootElement.GetProperty("sources");
        Assert.Equal("succeeded", sources.GetProperty("adzuna").GetProperty("outcome").GetString());
        Assert.Equal("failed", sources.GetProperty("boards").GetProperty("outcome").GetString());
        Assert.Equal("kaboom", sources.GetProperty("boards").GetProperty("note").GetString());
        Assert.Equal("skipped", sources.GetProperty("jobbank").GetProperty("outcome").GetString());
        Assert.DoesNotContain("SECRET", json);
    }

    [Fact]
    public async Task Cancelling_mid_run_still_writes_a_manifest_marked_cancelled()
    {
        using var tmp = new TempDir();
        using var cts = new CancellationTokenSource();
        var cancels = new FakeSource("adzuna", _ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var code = await Runner(cancels, Succeeds("boards")).RunAsync(tmp.Path, dryRun: false, cts.Token);

        Assert.Equal(ExitCodes.AllFailed, code);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(tmp.Path, "raw", "2026-09-29_0700", "manifest.json")));
        Assert.True(doc.RootElement.GetProperty("cancelled").GetBoolean());
        var sources = doc.RootElement.GetProperty("sources");
        Assert.Equal("Run cancelled.", sources.GetProperty("adzuna").GetProperty("note").GetString());
        Assert.False(sources.TryGetProperty("boards", out _)); // never started
    }

    [Fact]
    public async Task A_source_that_reports_failure_is_failed_even_with_successful_requests()
    {
        using var tmp = new TempDir();
        var unusable = new FakeSource("jobbank", ctx =>
        {
            ctx.Manifest.Record(new RequestRecord("jobbank", "https://a.test/catalog", 200, 10, 1, "raw/c.json", null));
            return Task.FromResult(SourceResult.Fail("Unexpected catalog format"));
        });

        await Runner(unusable).RunAsync(tmp.Path, dryRun: false, CancellationToken.None);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(tmp.Path, "raw", "2026-09-29_0700", "manifest.json")));
        Assert.Equal("failed", doc.RootElement.GetProperty("sources").GetProperty("jobbank").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Returns_1_when_nothing_was_collected()
    {
        using var tmp = new TempDir();
        var code = await Runner(Throws("adzuna"), Skips("boards")).RunAsync(tmp.Path, dryRun: false, CancellationToken.None);
        Assert.Equal(ExitCodes.AllFailed, code);
    }

    [Fact]
    public async Task Dry_run_writes_to_the_dryrun_folder()
    {
        using var tmp = new TempDir();
        await Runner(Succeeds("adzuna")).RunAsync(tmp.Path, dryRun: true, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(tmp.Path, "raw", "_dryrun", "manifest.json")));
    }

    [Fact]
    public async Task Creates_inbox_readme_and_counts_txt_files()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "inbox"));
        File.WriteAllText(Path.Combine(tmp.Path, "inbox", "a.txt"), "x");
        File.WriteAllText(Path.Combine(tmp.Path, "inbox", "b.txt"), "x");
        File.WriteAllText(Path.Combine(tmp.Path, "inbox", "c.md"), "x");

        await Runner(Succeeds("adzuna")).RunAsync(tmp.Path, dryRun: false, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(tmp.Path, "inbox", "README.md")));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(tmp.Path, "raw", "2026-09-29_0700", "manifest.json")));
        Assert.Equal(2, doc.RootElement.GetProperty("inboxFiles").GetInt32());
    }
}
