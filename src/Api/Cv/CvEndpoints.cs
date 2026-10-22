using Api.Applications;
using Microsoft.EntityFrameworkCore;
using Tracker.Data;
using Tracker.Interpreting;

namespace Api.Cv;

// Tailor CV: status of the master CV, the untailored full CV, a proposal per entry, and generating the file. They sit in
// the tracker group, so the gate, the dashboard-header check and the JSON mirror apply as for every tracker write.
public static class CvEndpoints
{
    public const string GeneratedLabel = "Tailored CV";

    public static void MapCvEndpoints(this RouteGroupBuilder tracker)
    {
        tracker.MapGet("/cv", (CvSettings cv, MarketSettings settings, ILoggerFactory loggers) =>
        {
            var load = Load(cv, settings, loggers);
            return new CvStatusView(load.Ready, load.Problems);
        });

        tracker.MapGet("/cv/full", (CvSettings cvSettings, MarketSettings settings, ILoggerFactory loggers) =>
        {
            var cv = Require(cvSettings, settings, loggers);
            var everything = new CvChoices(cv.Headline, cv.Summaries[0].Text, [.. cv.Bullets.Select(b => b.Id)]);
            return Results.File(CvDocx.Render(CvLayout.Paragraphs(cv, everything, new HashSet<string>())), CvDocx.ContentType,
                CvDocx.FileName(cv.Person.Name, null));
        });

        tracker.MapGet("/applications/{id:guid}/cv", async (Guid id, ApplicationsDb db, TrackerDb marketDb,
            CvSettings cvSettings, MarketSettings settings, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var entry = await db.Applications.AsNoTracking().Include(a => a.Company).Include(a => a.Documents).AsSplitQuery()
                .FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new TrackerNotFoundException(Tracking.MissingEntry);
            var cv = Require(cvSettings, settings, loggers);
            var text = await EntryTexts.ResolveAsync(entry, marketDb, ct);
            var last = entry.Documents.Where(d => d.CvChoices is not null).OrderByDescending(d => d.CreatedAt)
                .Select(d => (Choices: CvChoicesJson.Read(d.CvChoices), d.SentOn)).FirstOrDefault(d => d.Choices is not null);
            return CvTailor.Propose(cv, settings.Catalog, settings.Profile,
                new TailorInput(entry.Id, entry.Title, entry.Company!.Name, text.Text, text.Source, text.Snippet, entry.Version),
                last.Choices is { } choices ? (choices, last.SentOn) : null);
        });

        tracker.MapPost("/applications/{id:guid}/cv", async (Guid id, GenerateRequest body, ApplicationsDb db, TrackerDb marketDb,
            TrackerMirror mirror, CvSettings cvSettings, MarketSettings settings, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var entry = await db.Applications.Include(a => a.Company).FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new TrackerNotFoundException(Tracking.MissingEntry);
            var cv = Require(cvSettings, settings, loggers);
            var choices = CvRules.Check(body, cv);
            var text = await EntryTexts.ResolveAsync(entry, marketDb, ct);
            var wanted = settings.Catalog.Match(SkillText.ForMatching(entry.Title, text.Text));

            using var activity = TrackerTelemetry.Source.StartActivity("tracker.cv.generate");
            var bytes = CvDocx.Render(CvLayout.Paragraphs(cv, choices, wanted));
            activity?.SetTag("tracker.entry_id", id);
            activity?.SetTag("tracker.cv.bullets", choices.BulletIds.Count);
            activity?.SetTag("tracker.cv.bytes", bytes.Length);

            var now = clock.GetUtcNow();
            var document = new ApplicationDocument
            {
                Id = Guid.NewGuid(), ApplicationId = id, Kind = DocumentKind.Cv, Label = GeneratedLabel,
                FileName = CvDocx.FileName(cv.Person.Name, entry.Company!.Name), ContentType = CvDocx.ContentType, SizeBytes = bytes.Length,
                SentOn = Tracking.Today(clock), CreatedAt = now, CvChoices = CvChoicesJson.Write(choices),
            };
            // File first, row second, as for uploads: a failed save removes the file.
            var path = mirror.DocumentPath(document);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, ct);
            try
            {
                db.Documents.Add(document);
                entry.UpdatedAt = now;
                await Tracking.CommitAsync(db, mirror, "generate-cv", ct);
            }
            catch
            {
                File.Delete(path);
                throw;
            }
            return Results.Created($"/api/tracker/documents/{document.Id}", new GeneratedCv(document.Id, document.FileName));
        });
    }

    private static CvLoad Load(CvSettings cv, MarketSettings settings, ILoggerFactory loggers)
    {
        var load = MasterCvFile.Load(cv.Path, settings.Catalog);
        // The count only: problems quote the owner's CV, which stays out of the logs.
        if (!load.Ready) loggers.CreateLogger("Cv").LogWarning("The master CV has {Count} problems", load.Problems.Count);
        return load;
    }

    private static MasterCv Require(CvSettings cv, MarketSettings settings, ILoggerFactory loggers)
    {
        var load = Load(cv, settings, loggers);
        return load.Cv ?? throw new TrackerInputException("cv", string.Join(" ", load.Problems));
    }
}
