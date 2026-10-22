using Api.Market;
using Microsoft.EntityFrameworkCore;
using Tracker.Data;

namespace Api.Applications;

// Creates or updates the tracker tables once, on the first tracker request, then restores from the JSON copy if they
// are empty. Market endpoints never wait on this.
public sealed class TrackerSchema(IServiceScopeFactory scopes, TrackerMirror mirror, ILogger<TrackerSchema> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _ready;

    public bool Ready => _ready;

    // The tables turned out to be missing after all: check and restore again on the next tracker request.
    public void Reset() => _ready = false;

    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_ready) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_ready) return;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDb>();
            try
            {
                await db.Database.MigrateAsync(ct);
                var restored = await mirror.RestoreIfEmptyAsync(db, ct);
                if (restored > 0) log.LogInformation("Restored {Count} applications from {Path}", restored, mirror.FilePath);
            }
            catch (Exception ex) when (DatabaseErrors.IsUnavailable(ex))
            {
                throw new DatabaseUnavailableException(ex);
            }
            _ready = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
