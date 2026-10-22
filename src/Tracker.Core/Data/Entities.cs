namespace Tracker.Data;

public sealed class RunEntity
{
    public string RunId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
}

public sealed class PostingEntity
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Company { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string LocationText { get; set; } = "";
    public string? City { get; set; }
    public bool IsLocal { get; set; }
    public bool IsRemoteInCountry { get; set; }
    public bool InScope { get; set; }
    public string RoleFamily { get; set; } = "";
    public string Seniority { get; set; } = "";
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string? SalaryPeriod { get; set; }
    public string SalaryOrigin { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsFullText { get; set; }
    public bool CitizenshipOrClearance { get; set; }
    public string FirstSeenRun { get; set; } = "";
    public string LastSeenRun { get; set; } = "";
    public string? DuplicateOf { get; set; }
    public List<SightingEntity> Sightings { get; set; } = [];
    public List<PostingSkillEntity> Skills { get; set; } = [];
}

public sealed class SightingEntity
{
    public string PostingId { get; set; } = "";
    public string RunId { get; set; } = "";
    public RunEntity? Run { get; set; }
}

public sealed class SkillEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
}

public sealed class PostingSkillEntity
{
    public string PostingId { get; set; } = "";
    public int SkillId { get; set; }
    public SkillEntity? Skill { get; set; }
}

public sealed class AdzunaAggregateEntity
{
    public string RunId { get; set; } = "";
    public string Term { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Payload { get; set; } = "";
}

public sealed class JobBankRegionEntity
{
    public DateOnly Month { get; set; }
    public string EconomicRegion { get; set; } = "";
    public int Postings { get; set; }
    public int Vacancies { get; set; }
}

public sealed class JobBankMonthEntity
{
    public DateOnly Month { get; set; }
    public string NocCode { get; set; } = "";
    public string NocName { get; set; } = "";
    public int Postings { get; set; }
    public int Vacancies { get; set; }
    public decimal? WageMinMedian { get; set; }
    public decimal? WageMaxMedian { get; set; }
}
