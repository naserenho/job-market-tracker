using System.Text.Json;
using Archiver.Manifest;
using Archiver.Tests.TestSupport;

namespace Archiver.Tests.Manifest;

public class ManifestBuilderTests
{
    private static ManifestBuilder NewBuilder() => new("2026-09-29_0700", false, "test", DateTimeOffset.UnixEpoch, 80, "abu-dhabi");

    private static RequestRecord Ok(string source) => new(source, "https://a.test/x", 200, 10, 5, "raw/x.json", null);
    private static RequestRecord Bad(string source) => new(source, "https://a.test/y", 500, 0, 5, null, "HTTP 500");

    [Theory]
    [InlineData(3, 0, SourceOutcome.Succeeded)]
    [InlineData(2, 1, SourceOutcome.Partial)]
    [InlineData(0, 2, SourceOutcome.Failed)]
    [InlineData(0, 0, SourceOutcome.Failed)]
    public void Outcome_follows_ok_and_failed_counts(int ok, int failed, SourceOutcome expected) =>
        Assert.Equal(expected, Outcomes.From(ok, failed));

    [Fact]
    public void Exit_code_is_0_when_anything_succeeded_or_partially_succeeded()
    {
        Assert.Equal(ExitCodes.Ok, Outcomes.ExitCode([SourceOutcome.Failed, SourceOutcome.Partial]));
        Assert.Equal(ExitCodes.AllFailed, Outcomes.ExitCode([SourceOutcome.Failed, SourceOutcome.Skipped]));
        Assert.Equal(ExitCodes.AllFailed, Outcomes.ExitCode([]));
    }

    [Fact]
    public void CompleteSource_counts_only_that_sources_requests()
    {
        var m = NewBuilder();
        m.Record(Ok("adzuna"));
        m.Record(Bad("adzuna"));
        m.Record(Ok("boards"));

        var summary = m.CompleteSource("adzuna");

        Assert.Equal(new SourceSummary(SourceOutcome.Partial, 1, 1, null), summary);
    }

    [Fact]
    public void FailSource_after_some_successes_is_partial_and_scrubbed()
    {
        var m = NewBuilder();
        m.Record(Ok("adzuna"));

        var summary = m.FailSource("adzuna", "crashed at https://a.test/?app_key=SECRET");

        Assert.Equal(SourceOutcome.Partial, summary.Outcome);
        Assert.DoesNotContain("SECRET", summary.Note);
    }

    [Fact]
    public void Record_redacts_urls_and_scrubs_errors()
    {
        var m = NewBuilder();
        m.Record(new RequestRecord("adzuna", "https://a.test/s?app_id=ID9&app_key=KEY9&what=x", null, 0, 1, null, "failed: https://a.test/s?app_key=KEY9"));

        var json = JsonSerializer.Serialize(m.Build(DateTimeOffset.UnixEpoch), ManifestWriter.Json);

        Assert.DoesNotContain("KEY9", json);
        Assert.DoesNotContain("ID9", json);
    }

    [Fact]
    public async Task Record_is_safe_under_concurrency()
    {
        var m = NewBuilder();
        await Parallel.ForAsync(0, 500, (i, _) => { m.Record(Ok("boards")); return ValueTask.CompletedTask; });
        Assert.Equal(500, m.Build(DateTimeOffset.UnixEpoch).Requests.Count);
    }

    [Fact]
    public async Task WriteAsync_writes_camelCase_json_with_string_enums()
    {
        using var tmp = new TempDir();
        var m = NewBuilder();
        m.Record(Ok("adzuna"));
        m.CompleteSource("adzuna");
        m.SkipSource("jobbank", "offline");
        var path = Path.Combine(tmp.Path, "manifest.json");

        await ManifestWriter.WriteAsync(m.Build(DateTimeOffset.UnixEpoch), path, CancellationToken.None);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("succeeded", doc.RootElement.GetProperty("sources").GetProperty("adzuna").GetProperty("outcome").GetString());
        Assert.Equal("skipped", doc.RootElement.GetProperty("sources").GetProperty("jobbank").GetProperty("outcome").GetString());
        Assert.Equal(80, doc.RootElement.GetProperty("adzunaBudget").GetInt32());
    }

    // The processor reads only the active region's collections; old manifests without the field count as Toronto.
    [Fact]
    public void The_manifest_records_the_region() =>
        Assert.Contains("\"region\": \"abu-dhabi\"", JsonSerializer.Serialize(NewBuilder().Build(DateTimeOffset.UnixEpoch), ManifestWriter.Json));

    [Fact]
    public void A_source_the_region_does_not_use_is_recorded_as_not_applicable()
    {
        var builder = NewBuilder();
        builder.NotApplicable("adzuna", "Adzuna has no market for this region.");

        var json = JsonSerializer.Serialize(builder.Build(DateTimeOffset.UnixEpoch), ManifestWriter.Json);

        Assert.Contains("\"outcome\": \"notApplicable\"", json);
    }
}
