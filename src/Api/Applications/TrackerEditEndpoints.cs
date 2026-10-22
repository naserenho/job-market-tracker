using Microsoft.EntityFrameworkCore;

namespace Api.Applications;

// Every editable field is sent on each save; a field left out is cleared.
public sealed record ApplicationEdit(uint Version, string? Title, string? Url, string? Location, string? FoundVia, string? WorkModel,
    string? OfficeDays, string? TeamSize, string? WorkingHours, string? SalaryPosted, string? SalaryAsked, string? SalaryOffered, string? Notes);

public sealed record CompanyEdit(uint Version, string? Name, string? Website, string? OfficeLocation, string? CompanySize,
    string? AnnualLeave, string? Benefits, string? Notes);

public sealed record StatusChange(string? Status, DateOnly? On, string? Note);

public sealed record FollowUpInput(string? Action, DateOnly? DueOn, DateOnly? DoneOn);

public sealed record ContactInput(string? Name, string? Role, string? Email, string? Phone, string? LinkedIn, string? Notes);

public sealed record PostingTextEdit(uint Version, string? PostingText);

public static class TrackerEditEndpoints
{
    public static void MapTrackerEdits(this RouteGroupBuilder tracker)
    {
        tracker.MapPatch("/applications/{id:guid}", async (Guid id, ApplicationEdit body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            // The version the editor saw: if the row changed since, the UPDATE matches nothing and EF throws (409).
            db.Entry(entry).Property(e => e.Version).OriginalValue = body.Version;
            entry.Title = Rules.SingleLine(Rules.Required(body.Title, "title"));
            entry.Url = Rules.OptionalUrl(body.Url, "url");
            entry.Location = Line(body.Location, "location");
            entry.FoundVia = Line(body.FoundVia, "foundVia");
            entry.WorkModel = string.IsNullOrWhiteSpace(body.WorkModel) ? WorkModel.Unknown : Rules.ParseEnum<WorkModel>(body.WorkModel, "workModel");
            entry.OfficeDays = Line(body.OfficeDays, "officeDays");
            entry.TeamSize = Line(body.TeamSize, "teamSize");
            entry.WorkingHours = Line(body.WorkingHours, "workingHours");
            entry.SalaryPosted = Line(body.SalaryPosted, "salaryPosted");
            entry.SalaryAsked = Line(body.SalaryAsked, "salaryAsked");
            entry.SalaryOffered = Line(body.SalaryOffered, "salaryOffered");
            entry.Notes = Rules.Optional(body.Notes, "notes", Rules.MaxLongText);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "edit-application", ct);
            return Results.NoContent();
        });

        tracker.MapPatch("/companies/{id:guid}", async (Guid id, CompanyEdit body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new TrackerNotFoundException("That company doesn't exist.");
            db.Entry(company).Property(c => c.Version).OriginalValue = body.Version;
            var name = Rules.SingleLine(Rules.Required(body.Name, "name"));
            var key = Rules.CompanyKey(name);
            var clash = await db.Companies.AsNoTracking().Where(c => c.NameKey == key && c.Id != id).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (clash is { } otherId) throw new TrackerConflictException($"Another company in your tracker is already called '{name}'.", otherId);
            company.Name = name;
            company.NameKey = key;
            company.Website = Rules.OptionalUrl(body.Website, "website");
            company.OfficeLocation = Line(body.OfficeLocation, "officeLocation");
            company.CompanySize = Line(body.CompanySize, "companySize");
            company.AnnualLeave = Line(body.AnnualLeave, "annualLeave");
            company.Benefits = Rules.Optional(body.Benefits, "benefits", Rules.MaxLongText);
            company.Notes = Rules.Optional(body.Notes, "notes", Rules.MaxLongText);
            company.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "edit-company", ct);
            return Results.NoContent();
        });

        tracker.MapPut("/applications/{id:guid}/posting-text", async (Guid id, PostingTextEdit body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            db.Entry(entry).Property(e => e.Version).OriginalValue = body.Version;
            // Saved on the entry only, never in data/inbox: the next rebuild must not count the job twice.
            entry.PostingText = Rules.NoNul(Rules.Optional(body.PostingText, "postingText", Rules.MaxPostingText), "postingText");
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "posting-text", ct);
            return Results.NoContent();
        });

        tracker.MapPost("/applications/{id:guid}/status", async (Guid id, StatusChange body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var to = Rules.ParseEnum<ApplicationStatus>(Rules.Required(body.Status, "status"), "status");
            var today = Tracking.Today(clock);
            var on = body.On ?? today;
            if (on > today) throw new TrackerInputException("on", "The date can't be in the future.");
            var now = clock.GetUtcNow();
            db.Events.Add(new ApplicationEvent { Id = Guid.NewGuid(), ApplicationId = id, OnDate = on, FromStatus = entry.Status, ToStatus = to,
                Note = Rules.Optional(body.Note, "note", Rules.MaxLongText), CreatedAt = now });
            entry.Status = to;
            entry.UpdatedAt = now;
            await Tracking.CommitAsync(db, mirror, "status", ct);
            return Results.NoContent();
        });

        tracker.MapPost("/applications/{id:guid}/follow-ups", async (Guid id, FollowUpInput body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var followUp = new FollowUp { Id = Guid.NewGuid(), ApplicationId = id, CreatedAt = clock.GetUtcNow() };
            Apply(followUp, body);
            db.FollowUps.Add(followUp);
            entry.UpdatedAt = followUp.CreatedAt;
            await Tracking.CommitAsync(db, mirror, "add-follow-up", ct);
            return Results.Created($"/api/tracker/applications/{id}/follow-ups/{followUp.Id}", new NewEntry(followUp.Id));
        });

        tracker.MapPatch("/applications/{id:guid}/follow-ups/{followUpId:guid}", async (Guid id, Guid followUpId, FollowUpInput body,
            ApplicationsDb db, TrackerMirror mirror, TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var followUp = await db.FollowUps.FirstOrDefaultAsync(f => f.Id == followUpId && f.ApplicationId == id, ct)
                ?? throw new TrackerNotFoundException("That follow-up doesn't exist.");
            Apply(followUp, body);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "edit-follow-up", ct);
            return Results.NoContent();
        });

        tracker.MapDelete("/applications/{id:guid}/follow-ups/{followUpId:guid}", async (Guid id, Guid followUpId, ApplicationsDb db,
            TrackerMirror mirror, TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var followUp = await db.FollowUps.FirstOrDefaultAsync(f => f.Id == followUpId && f.ApplicationId == id, ct)
                ?? throw new TrackerNotFoundException("That follow-up doesn't exist.");
            db.FollowUps.Remove(followUp);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "delete-follow-up", ct);
            return Results.NoContent();
        });

        tracker.MapPost("/applications/{id:guid}/contacts", async (Guid id, ContactInput body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var contact = new Contact { Id = Guid.NewGuid(), ApplicationId = id, CreatedAt = clock.GetUtcNow() };
            Apply(contact, body);
            db.Contacts.Add(contact);
            entry.UpdatedAt = contact.CreatedAt;
            await Tracking.CommitAsync(db, mirror, "add-contact", ct);
            return Results.Created($"/api/tracker/applications/{id}/contacts/{contact.Id}", new NewEntry(contact.Id));
        });

        tracker.MapPatch("/applications/{id:guid}/contacts/{contactId:guid}", async (Guid id, Guid contactId, ContactInput body,
            ApplicationsDb db, TrackerMirror mirror, TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId && c.ApplicationId == id, ct)
                ?? throw new TrackerNotFoundException("That contact doesn't exist.");
            Apply(contact, body);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "edit-contact", ct);
            return Results.NoContent();
        });

        tracker.MapDelete("/applications/{id:guid}/contacts/{contactId:guid}", async (Guid id, Guid contactId, ApplicationsDb db,
            TrackerMirror mirror, TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId && c.ApplicationId == id, ct)
                ?? throw new TrackerNotFoundException("That contact doesn't exist.");
            db.Contacts.Remove(contact);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "delete-contact", ct);
            return Results.NoContent();
        });
    }

    private static string Line(string? value, string field) => Rules.SingleLine(Rules.Optional(value, field));

    private static void Apply(FollowUp followUp, FollowUpInput body)
    {
        followUp.Action = Rules.SingleLine(Rules.Required(body.Action, "action"));
        followUp.DueOn = body.DueOn ?? throw new TrackerInputException("dueOn", "Due date is required.");
        followUp.DoneOn = body.DoneOn;
    }

    private static void Apply(Contact contact, ContactInput body)
    {
        contact.Name = Rules.SingleLine(Rules.Required(body.Name, "name"));
        contact.Role = Line(body.Role, "role");
        contact.Email = Rules.OptionalEmail(body.Email, "email");
        contact.Phone = Line(body.Phone, "phone");
        contact.LinkedIn = Rules.OptionalUrl(body.LinkedIn, "linkedIn");
        contact.Notes = Rules.Optional(body.Notes, "notes", Rules.MaxLongText);
    }
}
