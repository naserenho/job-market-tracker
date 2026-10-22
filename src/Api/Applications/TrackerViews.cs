namespace Api.Applications;

public sealed record NextAction(Guid Id, string Action, DateOnly DueOn);

public sealed record ApplicationRow(Guid Id, string Title, string Company, ApplicationStatus Status, NextAction? Next,
    DateTimeOffset UpdatedAt, string? PostingId, bool Live);

public sealed record UpNextItem(Guid FollowUpId, Guid ApplicationId, string Title, string Company, string Action, DateOnly DueOn, bool Overdue);

public sealed record IndexEntry(Guid Id, ApplicationStatus Status);

public sealed record CompanyOption(Guid Id, string Name);

public sealed record CompanyView(Guid Id, string Name, string Website, string OfficeLocation, string CompanySize, string AnnualLeave,
    string Benefits, string Notes, uint Version, int Applications);

public sealed record EventView(Guid Id, DateOnly OnDate, ApplicationStatus? FromStatus, ApplicationStatus ToStatus, string Note);

public sealed record FollowUpView(Guid Id, string Action, DateOnly DueOn, DateOnly? DoneOn);

public sealed record ContactView(Guid Id, string Name, string Role, string Email, string Phone, string LinkedIn, string Notes);

// Generated: made by the Tailor CV page (it has saved choices); otherwise an uploaded file, the one actually sent.
public sealed record DocumentView(Guid Id, DocumentKind Kind, string Label, string FileName, long SizeBytes, DateOnly SentOn, bool Generated);

// What the market data says about the linked posting right now.
public sealed record MarketFacts(double Match, bool Live, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, IReadOnlyList<string> Skills);

// Where the entry's posting stands in the market data: counted, not seen by a rebuild yet, or left out by one
// (outside the GTA and remote-Canada scope, or the same job as another posting).
public enum MarketState { Counted, Pending, OutOfScope, Duplicate }

public sealed record ApplicationDetail(Guid Id, string Title, string Url, string Location, ApplicationStatus Status, EntryOrigin Origin,
    string FoundVia, WorkModel WorkModel, string OfficeDays, string TeamSize, string WorkingHours, string SalaryPosted, string SalaryAsked,
    string SalaryOffered, string Notes, string? PostingId, string PostingText, MarketFacts? Market, MarketState? MarketState, CompanyView Company,
    IReadOnlyList<EventView> Events, IReadOnlyList<FollowUpView> FollowUps, IReadOnlyList<ContactView> Contacts,
    IReadOnlyList<DocumentView> Documents, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, uint Version);

public sealed record NewEntry(Guid Id);

public sealed record ShortlistRequest(string? PostingId, string? Status);

public sealed record PasteRequest(string? Title, string? Company, string? Url, string? Location, string? PostingText, string? Status);
