using System.Text.Json.Serialization;

namespace Api.Applications;

public enum ApplicationStatus { Shortlisted, Applied, Screening, Interviewing, Offer, Accepted, Rejected, Withdrawn }

public enum WorkModel { Unknown, Remote, Hybrid, Onsite }

public enum DocumentKind { Cv, CoverLetter, Other }

public enum EntryOrigin { Market, Pasted }

// Shared by every application to the same company; NameKey ("rbc") makes "RBC" and "rbc " one company.
public sealed class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string Website { get; set; } = "";
    public string OfficeLocation { get; set; } = "";
    public string CompanySize { get; set; } = "";
    public string AnnualLeave { get; set; } = "";
    public string Benefits { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Postgres xmin: changes on every update, so a save that carries an older value is refused (see ADR 0011).
    [JsonIgnore] public uint Version { get; set; }
}

// One job the owner shortlisted or applied to. It keeps its own copy of the posting's title, link and location, so it
// outlives the posting in the market data.
public sealed class JobApplication
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string? PostingId { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Location { get; set; } = "";
    public string PostingText { get; set; } = "";
    public ApplicationStatus Status { get; set; }
    public EntryOrigin Origin { get; set; }
    public string FoundVia { get; set; } = "";
    public WorkModel WorkModel { get; set; }
    public string OfficeDays { get; set; } = "";
    public string TeamSize { get; set; } = "";
    public string WorkingHours { get; set; } = "";
    public string SalaryPosted { get; set; } = "";
    public string SalaryAsked { get; set; } = "";
    public string SalaryOffered { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore] public uint Version { get; set; }
    [JsonIgnore] public Company? Company { get; set; }
    [JsonIgnore] public List<ApplicationEvent> Events { get; set; } = [];
    [JsonIgnore] public List<FollowUp> FollowUps { get; set; } = [];
    [JsonIgnore] public List<Contact> Contacts { get; set; } = [];
    [JsonIgnore] public List<ApplicationDocument> Documents { get; set; } = [];
}

// One line of an application's timeline: a status change (or the entry's creation, with FromStatus null).
public sealed class ApplicationEvent
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateOnly OnDate { get; set; }
    public ApplicationStatus? FromStatus { get; set; }
    public ApplicationStatus ToStatus { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FollowUp
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Action { get; set; } = "";
    public DateOnly DueOn { get; set; }
    public DateOnly? DoneOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Contact
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string LinkedIn { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

// A file sent with an application; the bytes live in data/tracker/files, not in the database.
public sealed class ApplicationDocument
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DocumentKind Kind { get; set; }
    public string Label { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateOnly SentOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    // The choices a generated CV was made from (JSON); null for uploaded files (spec "Saving: generated is not sent").
    public string? CvChoices { get; set; }
}
