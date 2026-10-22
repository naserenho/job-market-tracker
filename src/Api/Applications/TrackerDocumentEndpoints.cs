using Microsoft.EntityFrameworkCore;

namespace Api.Applications;

public static class TrackerDocumentEndpoints
{
    public static void MapTrackerDocuments(this RouteGroupBuilder tracker)
    {
        tracker.MapPost("/applications/{id:guid}/documents", async (Guid id, HttpRequest request, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            if (!request.HasFormContentType) throw new TrackerInputException("file", "Send the document as a file upload.");
            // Refuse an oversized upload from its headers, before reading the body: Kestrel's own 30 MB request limit would
            // otherwise end it with a generic error (or a dropped connection) instead of this message.
            if (request.ContentLength > Rules.MaxFileBytes + 64 * 1024) throw new TrackerInputException("file", "Files can be up to 10 MB.");
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? throw new TrackerInputException("file", "Choose a file to upload.");
            Rules.CheckUpload(file.FileName, file.Length);
            string? kindText = form["kind"];
            string? labelText = form["label"];
            string? sentOnText = form["sentOn"];
            var kind = string.IsNullOrWhiteSpace(kindText) ? DocumentKind.Other : Rules.ParseEnum<DocumentKind>(kindText, "kind");
            var sentOn = string.IsNullOrWhiteSpace(sentOnText) ? Tracking.Today(clock)
                : DateOnly.TryParseExact(sentOnText, "yyyy-MM-dd", out var parsed) ? parsed
                : throw new TrackerInputException("sentOn", "Use a date like 2027-03-01.");
            var given = Rules.SingleLine(Rules.Optional(labelText, "label"));
            var fallback = Rules.SingleLine(Path.GetFileNameWithoutExtension(file.FileName.Replace('\\', '/')));
            var document = new ApplicationDocument
            {
                Id = Guid.NewGuid(), ApplicationId = id, Kind = kind, FileName = Rules.SafeFileName(file.FileName),
                ContentType = Rules.ContentType(file.FileName), SizeBytes = file.Length, SentOn = sentOn, CreatedAt = clock.GetUtcNow(),
                Label = given.Length > 0 ? given : fallback.Length > 0 ? fallback[..Math.Min(fallback.Length, Rules.MaxText)] : "Document",
            };

            // File first, row second: a failed save removes the file, so neither is ever left without the other.
            var path = mirror.DocumentPath(document);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var target = File.Create(path)) await file.CopyToAsync(target, ct);
            try
            {
                db.Documents.Add(document);
                entry.UpdatedAt = document.CreatedAt;
                await Tracking.CommitAsync(db, mirror, "add-document", ct);
            }
            catch
            {
                File.Delete(path);
                throw;
            }
            return Results.Created($"/api/tracker/documents/{document.Id}", new NewEntry(document.Id));
        });

        tracker.MapGet("/documents/{documentId:guid}", async (Guid documentId, ApplicationsDb db, TrackerMirror mirror, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct)
                ?? throw new TrackerNotFoundException("That document doesn't exist.");
            var path = mirror.DocumentPath(document);
            if (!File.Exists(path)) throw new TrackerNotFoundException($"The file for '{document.Label}' is missing from {mirror.FilesFolder}.");
            return Results.File(path, document.ContentType, document.FileName);
        });

        tracker.MapDelete("/documents/{documentId:guid}", async (Guid documentId, ApplicationsDb db, TrackerMirror mirror, TimeProvider clock,
            ILoggerFactory loggers, CancellationToken ct) =>
        {
            var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct)
                ?? throw new TrackerNotFoundException("That document doesn't exist.");
            var entry = await Tracking.FindEntryAsync(db, document.ApplicationId, ct);
            db.Documents.Remove(document);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "delete-document", ct);
            Remove(mirror.DocumentPath(document), loggers);
            return Results.NoContent();
        });

        tracker.MapDelete("/applications/{id:guid}", async (Guid id, ApplicationsDb db, TrackerMirror mirror, ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            db.Applications.Remove(entry); // its timeline, follow-ups, contacts and documents cascade in the database
            await Tracking.CommitAsync(db, mirror, "delete-application", ct);
            Remove(mirror.ApplicationFolder(id), loggers);
            return Results.NoContent();
        });
    }

    // The database change is already committed; a file that can't be removed is logged, not reported as a failure.
    private static void Remove(string path, ILoggerFactory loggers)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            loggers.CreateLogger("Tracker").LogWarning(ex, "Could not remove {Path}", path);
        }
    }
}
