using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Api.Applications;
using Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class TrackerMirrorTests(TrackerFixture tracker) : IDisposable
{
    private readonly TempDir _root = new();

    private TrackerMirror Mirror() => new(Data.Settings with { DataRoot = _root.Path }, tracker.Clock, NullLogger<TrackerMirror>.Instance);

    [Fact]
    public async Task The_copy_holds_what_the_database_holds()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        var companyId = Guid.NewGuid();
        await using var db = tracker.OpenDb();
        db.Companies.Add(new Company { Id = companyId, Name = "Mirror Test Co", NameKey = "mirror test co", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var mirror = Mirror();

        await mirror.WriteAsync(db);

        Assert.Contains(MirrorStore.Read(mirror.FilePath)!.Companies, c => c.Id == companyId);
        Assert.Equal((tracker.Clock.Now, (string?)null), (mirror.LastBackupAt!.Value, mirror.LastBackupError));
    }

    [Fact]
    public async Task A_failed_copy_is_reported_without_throwing()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        File.WriteAllText(Path.Combine(_root.Path, "tracker"), "a file where the folder should be");
        await using var db = tracker.OpenDb();
        var mirror = Mirror();

        await mirror.WriteAsync(db);

        Assert.Contains("could not be updated", mirror.LastBackupError);
        Assert.Null(mirror.LastBackupAt);
    }

    [Fact]
    public void Document_files_live_under_their_application_folder()
    {
        var mirror = Mirror();
        var document = new ApplicationDocument { Id = Guid.NewGuid(), ApplicationId = Guid.NewGuid(), FileName = "CV.pdf" };

        Assert.Equal(Path.Combine(_root.Path, "tracker", "files", document.ApplicationId.ToString("N"), $"{document.Id:N}-CV.pdf"),
            mirror.DocumentPath(document));
    }

    private sealed class IsolationSpy : DbTransactionInterceptor
    {
        public List<IsolationLevel> Seen { get; } = [];

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
            DbTransaction result, CancellationToken cancellationToken = default)
        {
            Seen.Add(result.IsolationLevel);
            return ValueTask.FromResult(result);
        }
    }

    // Backup spans started under `parent` (other test classes write backups at the same time).
    private static (ActivityListener Listener, Func<List<Activity>> Spans) BackupSpans(Activity parent)
    {
        var spans = new ConcurrentBag<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == TrackerTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return (listener, () => [.. spans.Where(s => s.OperationName == "tracker.backup" && s.ParentId == parent.Id)]);
    }

    [Fact]
    public async Task The_copy_is_read_from_one_snapshot_inside_a_traced_span()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        var spy = new IsolationSpy();
        using var parent = new Activity("test").Start();
        var (listener, spans) = BackupSpans(parent);
        using var _ = listener;
        await using var db = new ApplicationsDb(
            new DbContextOptionsBuilder<ApplicationsDb>(ApplicationsDb.Options(tracker.ConnectionString)).AddInterceptors(spy).Options);
        var mirror = Mirror();

        await mirror.WriteAsync(db);

        Assert.Null(mirror.LastBackupError);
        Assert.Equal(new[] { IsolationLevel.RepeatableRead }, spy.Seen);
        Assert.NotEqual(ActivityStatusCode.Error, Assert.Single(spans()).Status);
    }

    [Fact]
    public async Task A_failed_copy_marks_its_span_as_an_error()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        File.WriteAllText(Path.Combine(_root.Path, "tracker"), "a file where the folder should be");
        using var parent = new Activity("test").Start();
        var (listener, spans) = BackupSpans(parent);
        using var _ = listener;
        await using var db = tracker.OpenDb();

        await Mirror().WriteAsync(db);

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(spans()).Status);
    }

    public void Dispose() => _root.Dispose();
}
