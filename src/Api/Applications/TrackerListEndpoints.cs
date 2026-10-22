using Api.Market;
using Api.Queries;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tracker.Data;

namespace Api.Applications;

public static class TrackerListEndpoints
{
    // The unique indexes ApplicationsDb declares on companies.name_key and applications.posting_id.
    private const string CompanyIndex = "ix_companies_name_key", PostingIndex = "ix_applications_posting_id";

    public static void MapTrackerLists(this RouteGroupBuilder tracker)
    {
        tracker.MapGet("/applications", async (ApplicationsDb db, MarketData market, string? status, string? q, string? sort, CancellationToken ct) =>
        {
            var statuses = (status ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Rules.ParseEnum<ApplicationStatus>(s, "status")).ToHashSet();
            var order = string.IsNullOrEmpty(sort) ? "next"
                : sort is "next" or "updated" or "company" ? sort
                : throw new TrackerInputException("sort", $"Unknown sort '{sort}'. Use next, updated or company.");
            var term = (q ?? "").Trim();
            var live = LiveIds((await market.GetAsync(ct)).Data);
            var rows = (await db.Applications.AsNoTracking().Include(a => a.Company).Include(a => a.FollowUps).AsSplitQuery().ToListAsync(ct))
                .Where(a => statuses.Count == 0 || statuses.Contains(a.Status))
                .Where(a => term.Length == 0 || a.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || a.Company!.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(a => ToRow(a, live))
                .ToList();
            return order switch
            {
                "updated" => rows.OrderByDescending(r => r.UpdatedAt).ToList(),
                "company" => rows.OrderBy(r => r.Company, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList(),
                _ => rows.OrderBy(r => r.Next is null).ThenBy(r => r.Next?.DueOn).ThenByDescending(r => r.UpdatedAt).ToList(),
            };
        });

        tracker.MapGet("/up-next", async (ApplicationsDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var today = Tracking.Today(clock);
            var until = today.AddDays(7);
            var due = await db.FollowUps.AsNoTracking().Where(f => f.DoneOn == null && f.DueOn <= until).OrderBy(f => f.DueOn).ToListAsync(ct);
            var ids = due.Select(f => f.ApplicationId).Distinct().ToList();
            var entries = await db.Applications.AsNoTracking().Include(a => a.Company).Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
            return due.Select(f => new UpNextItem(f.Id, f.ApplicationId, entries[f.ApplicationId].Title, entries[f.ApplicationId].Company!.Name,
                f.Action, f.DueOn, f.DueOn < today)).ToList();
        });

        tracker.MapGet("/index", async (ApplicationsDb db, CancellationToken ct) =>
            (await db.Applications.AsNoTracking().Where(a => a.PostingId != null).Select(a => new { a.PostingId, a.Id, a.Status }).ToListAsync(ct))
                .ToDictionary(a => a.PostingId!, a => new IndexEntry(a.Id, a.Status), StringComparer.Ordinal));

        tracker.MapGet("/companies", async (ApplicationsDb db, CancellationToken ct) =>
            (await db.Companies.AsNoTracking().Select(c => new CompanyOption(c.Id, c.Name)).ToListAsync(ct))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList());

        tracker.MapGet("/applications/{id:guid}", async (Guid id, ApplicationsDb db, TrackerDb marketDb, MarketData market,
            MarketSettings settings, CancellationToken ct) =>
        {
            var entry = await db.Applications.AsNoTracking().Include(a => a.Company).Include(a => a.Events).Include(a => a.FollowUps)
                .Include(a => a.Contacts).Include(a => a.Documents).AsSplitQuery().FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new TrackerNotFoundException(Tracking.MissingEntry);
            var company = entry.Company!;
            var companyEntries = await db.Applications.CountAsync(a => a.CompanyId == company.Id, ct);
            var snapshot = (await market.GetAsync(ct)).Data;
            var posting = entry.PostingId is null ? null : snapshot.Postings.FirstOrDefault(p => p.Id == entry.PostingId);
            var facts = posting is null ? null : new MarketFacts(PostingQueries.Match(posting, settings.Profile),
                LiveIds(snapshot).Contains(posting.Id), posting.Sightings.Min(), posting.Sightings.Max(),
                [.. posting.Skills.Order(StringComparer.Ordinal)]);
            var text = (await EntryTexts.ResolveAsync(entry, marketDb, ct)).Text;
            var state = await EntryTexts.StateAsync(entry, posting is not null, marketDb, ct);
            return new ApplicationDetail(entry.Id, entry.Title, entry.Url, entry.Location, entry.Status, entry.Origin, entry.FoundVia,
                entry.WorkModel, entry.OfficeDays, entry.TeamSize, entry.WorkingHours, entry.SalaryPosted, entry.SalaryAsked,
                entry.SalaryOffered, entry.Notes, entry.PostingId, text, facts, state,
                new CompanyView(company.Id, company.Name, company.Website, company.OfficeLocation, company.CompanySize,
                    company.AnnualLeave, company.Benefits, company.Notes, company.Version, companyEntries),
                [.. entry.Events.OrderBy(e => e.OnDate).ThenBy(e => e.CreatedAt).Select(e => new EventView(e.Id, e.OnDate, e.FromStatus, e.ToStatus, e.Note))],
                [.. entry.FollowUps.OrderBy(f => f.DoneOn is not null).ThenBy(f => f.DueOn).Select(f => new FollowUpView(f.Id, f.Action, f.DueOn, f.DoneOn))],
                [.. entry.Contacts.OrderBy(c => c.CreatedAt).Select(c => new ContactView(c.Id, c.Name, c.Role, c.Email, c.Phone, c.LinkedIn, c.Notes))],
                [.. entry.Documents.OrderByDescending(d => d.SentOn).ThenByDescending(d => d.CreatedAt)
                    .Select(d => new DocumentView(d.Id, d.Kind, d.Label, d.FileName, d.SizeBytes, d.SentOn, d.CvChoices != null))],
                entry.CreatedAt, entry.UpdatedAt, entry.Version);
        });

        tracker.MapPost("/applications", async (ShortlistRequest body, ApplicationsDb db, MarketData market, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var postingId = Rules.Required(body.PostingId, "postingId", 300);
            var status = string.IsNullOrWhiteSpace(body.Status) ? ApplicationStatus.Shortlisted : Rules.ParseEnum<ApplicationStatus>(body.Status, "status");
            var existing = await db.Applications.AsNoTracking().Where(a => a.PostingId == postingId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            if (existing is { } existingId) throw new TrackerConflictException("This posting is already in your tracker.", existingId);
            var posting = (await market.GetAsync(ct)).Data.Postings.FirstOrDefault(p => p.Id == postingId)
                ?? throw new TrackerNotFoundException($"Posting '{postingId}' isn't in the current market data.");
            var company = await FindOrCreateCompanyAsync(db, posting.Company, clock, ct);
            var entry = CreateEntry(company, posting.Title, posting.Url, posting.Location, "", status, EntryOrigin.Market, postingId, clock,
                "Added from the market data");
            db.Applications.Add(entry);
            await CommitNewEntryAsync(db, mirror, entry, "shortlist", ct);
            return Results.Created($"/api/tracker/applications/{entry.Id}", new NewEntry(entry.Id));
        });

        tracker.MapPost("/applications/paste", async (PasteRequest body, ApplicationsDb db, MarketSettings settings, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var title = Rules.SingleLine(Rules.Required(body.Title, "title"));
            var companyName = Rules.SingleLine(Rules.Required(body.Company, "company"));
            var url = Rules.OptionalUrl(body.Url, "url");
            var location = Rules.SingleLine(Rules.Optional(body.Location, "location"));
            var text = Rules.Required(body.PostingText, "postingText", Rules.MaxPostingText);
            var status = string.IsNullOrWhiteSpace(body.Status) ? ApplicationStatus.Shortlisted : Rules.ParseEnum<ApplicationStatus>(body.Status, "status");
            var (postingId, file) = InboxWriter.Write(Path.Combine(settings.DataRoot, "inbox"), title, companyName, url, location, text, Tracking.Today(clock), settings.Region.Name);
            try
            {
                var company = await FindOrCreateCompanyAsync(db, companyName, clock, ct);
                var entry = CreateEntry(company, title, url, location, text, status, EntryOrigin.Pasted, postingId, clock, "Pasted into the tracker");
                db.Applications.Add(entry);
                await CommitNewEntryAsync(db, mirror, entry, "paste", ct);
                return Results.Created($"/api/tracker/applications/{entry.Id}", new NewEntry(entry.Id));
            }
            catch
            {
                File.Delete(file); // no entry, so no stray posting in the next rebuild
                throw;
            }
        });
    }

    // Postings seen in the latest collection: still open, as far as the market data knows.
    internal static IReadOnlySet<string> LiveIds(MarketSnapshot data) =>
        data.LatestRunAt is { } latest
            ? data.Postings.Where(p => p.Sightings.Contains(latest)).Select(p => p.Id).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    internal static async Task<Company> FindOrCreateCompanyAsync(ApplicationsDb db, string name, TimeProvider clock, CancellationToken ct)
    {
        var display = Rules.SingleLine(name);
        var key = Rules.CompanyKey(display);
        var existing = db.Companies.Local.FirstOrDefault(c => c.NameKey == key) ?? await db.Companies.FirstOrDefaultAsync(c => c.NameKey == key, ct);
        if (existing is not null) return existing;
        var now = clock.GetUtcNow();
        var company = new Company { Id = Guid.NewGuid(), Name = display, NameKey = key, CreatedAt = now, UpdatedAt = now };
        db.Companies.Add(company);
        return company;
    }

    // Saves a new entry. Two requests can pass the "already there?" checks at the same moment; the database's unique
    // indexes decide. A lost company race is retried once with the winner's company; a lost posting race is a 409 that
    // names the winner's entry. Anything else surfaces unchanged.
    internal static async Task CommitNewEntryAsync(ApplicationsDb db, TrackerMirror mirror, JobApplication entry, string operation, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await Tracking.CommitAsync(db, mirror, operation, ct);
                return;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation)
            {
                if (violation.ConstraintName == PostingIndex && entry.PostingId is { } postingId)
                {
                    db.ChangeTracker.Clear();
                    var existing = await db.Applications.AsNoTracking().Where(a => a.PostingId == postingId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
                    throw new TrackerConflictException("This posting is already in your tracker.", existing);
                }
                if (attempt > 1 || violation.ConstraintName != CompanyIndex || db.Entry(entry.Company!).State != EntityState.Added) throw;
                // Point the entry at the winner's company before letting go of ours: detaching first would leave the
                // entry without its required company.
                var mine = entry.Company!;
                var theirs = await db.Companies.FirstAsync(c => c.NameKey == mine.NameKey && c.Id != mine.Id, ct);
                entry.Company = theirs;
                entry.CompanyId = theirs.Id;
                db.Entry(mine).State = EntityState.Detached;
            }
        }
    }

    internal static JobApplication CreateEntry(Company company, string title, string url, string location, string postingText,
        ApplicationStatus status, EntryOrigin origin, string? postingId, TimeProvider clock, string note)
    {
        var now = clock.GetUtcNow();
        var entry = new JobApplication
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, Company = company, PostingId = postingId, Title = title, Url = url,
            Location = location, PostingText = postingText, Status = status, Origin = origin, CreatedAt = now, UpdatedAt = now,
        };
        entry.Events.Add(new ApplicationEvent { Id = Guid.NewGuid(), ApplicationId = entry.Id, OnDate = Tracking.Today(clock),
            ToStatus = status, Note = note, CreatedAt = now });
        return entry;
    }

    private static ApplicationRow ToRow(JobApplication a, IReadOnlySet<string> live)
    {
        var next = a.FollowUps.Where(f => f.DoneOn is null).OrderBy(f => f.DueOn).FirstOrDefault();
        return new ApplicationRow(a.Id, a.Title, a.Company!.Name, a.Status, next is null ? null : new NextAction(next.Id, next.Action, next.DueOn),
            a.UpdatedAt, a.PostingId, a.PostingId is not null && live.Contains(a.PostingId));
    }
}
