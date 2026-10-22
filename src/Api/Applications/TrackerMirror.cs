using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Tracker.Data;

namespace Api.Applications;

// Keeps data/tracker/tracker.json equal to the tracker tables, and brings them back from it when they are empty.
public sealed class TrackerMirror(MarketSettings settings, TimeProvider clock, ILogger<TrackerMirror> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string FilePath => Path.Combine(settings.DataRoot, "tracker", "tracker.json");
    public string FilesFolder => Path.Combine(settings.DataRoot, "tracker", "files");
    public DateTimeOffset? LastBackupAt { get; private set; }
    public string? LastBackupError { get; private set; }

    public string ApplicationFolder(Guid applicationId) => Path.Combine(FilesFolder, applicationId.ToString("N"));

    public string DocumentPath(ApplicationDocument document) =>
        Path.Combine(ApplicationFolder(document.ApplicationId), $"{document.Id:N}-{document.FileName}");

    // Called after every committed change. Never throws: the change is already saved, so a failed copy is reported
    // (status endpoint, dashboard banner) instead of failing the request.
    public async Task WriteAsync(ApplicationsDb db)
    {
        using var activity = TrackerTelemetry.Source.StartActivity("tracker.backup");
        await _gate.WaitAsync();
        try
        {
            // One snapshot: a change landing between two of these reads can't leave the copy half old, half new.
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            var file = new MirrorFile(MirrorFile.CurrentFormat, clock.GetUtcNow(),
                await db.Companies.AsNoTracking().OrderBy(c => c.CreatedAt).ToListAsync(),
                await db.Applications.AsNoTracking().OrderBy(a => a.CreatedAt).ToListAsync(),
                await db.Events.AsNoTracking().OrderBy(e => e.CreatedAt).ToListAsync(),
                await db.FollowUps.AsNoTracking().OrderBy(f => f.CreatedAt).ToListAsync(),
                await db.Contacts.AsNoTracking().OrderBy(c => c.CreatedAt).ToListAsync(),
                await db.Documents.AsNoTracking().OrderBy(d => d.CreatedAt).ToListAsync());
            await tx.CommitAsync();
            MirrorStore.Write(FilePath, file);
            activity?.SetTag("tracker.applications", file.Applications.Count);
            LastBackupAt = file.SavedAt;
            LastBackupError = null;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LastBackupError = $"The backup copy in {FilePath} could not be updated: {ex.Message}";
            log.LogError(ex, "Tracker backup failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    // When every tracker table is empty (a new or lost database) and the copy has entries, load it back in one
    // transaction, keeping ids. Returns the number of applications restored.
    public async Task<int> RestoreIfEmptyAsync(ApplicationsDb db, CancellationToken ct)
    {
        if (await db.Companies.AnyAsync(ct) || await db.Applications.AnyAsync(ct) || await db.Events.AnyAsync(ct)
            || await db.FollowUps.AnyAsync(ct) || await db.Contacts.AnyAsync(ct) || await db.Documents.AnyAsync(ct))
            return 0;
        var file = MirrorStore.Read(FilePath);
        if (file is null || (file.Companies.Count == 0 && file.Applications.Count == 0)) return 0;

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.Companies.AddRange(file.Companies);
            db.Applications.AddRange(file.Applications);
            db.Events.AddRange(file.Events);
            db.FollowUps.AddRange(file.FollowUps);
            db.Contacts.AddRange(file.Contacts);
            db.Documents.AddRange(file.Documents);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (!DatabaseErrors.IsUnavailable(ex))
        {
            // The copy reads as JSON but the database refuses its rows (an edited file, a row pointing at nothing).
            db.ChangeTracker.Clear();
            throw new TrackerBackupException(
                $"{FilePath} can't be restored ({ex.InnerException?.Message ?? ex.Message}). Fix or move the file; the tracker stays closed until then so the copy isn't overwritten.", ex);
        }
        db.ChangeTracker.Clear();
        return file.Applications.Count;
    }
}
