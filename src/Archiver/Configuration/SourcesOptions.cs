namespace Archiver.Configuration;

// Bound from config/sources.json, then the active region's file (Adzuna's country and place, Boards, JobBank);
// Adzuna AppId/AppKey arrive from user-secrets or env vars.
public sealed class SourcesOptions
{
    public string Region { get; set; } = "";
    // From the region file: SmartRecruiters lists are asked for this country's postings only.
    public string CountryCode { get; set; } = "";
    public AdzunaOptions Adzuna { get; set; } = new();
    public List<BoardConfig> Boards { get; set; } = [];
    public JobBankOptions JobBank { get; set; } = new();
}

public sealed class AdzunaOptions
{
    public string? AppId { get; set; }
    public string? AppKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.adzuna.com/v1/api/jobs";
    // From the region file. A region without an Adzuna section (no Adzuna market there) leaves Country empty.
    public string Country { get; set; } = "";
    public string Where { get; set; } = "";
    public int DistanceKm { get; set; }
    public List<string> Terms { get; set; } = [];
    public int ResultsPerPage { get; set; } = 50;
    public int MaxPages { get; set; } = 4;
    public int MaxCallsPerRun { get; set; } = 80;
    public int MinDelayMs { get; set; } = 3000;

    public bool HasCredentials => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppKey);
    public bool Enabled => !string.IsNullOrWhiteSpace(Country);
}

public sealed class BoardConfig
{
    public string Ats { get; set; } = "";
    public string Slug { get; set; } = "";
}

public sealed class JobBankOptions
{
    public string DatasetId { get; set; } = "";
    public string CatalogUrl { get; set; } = "https://open.canada.ca/data/api/action/package_show";
    public int BackfillMonths { get; set; } = 12;
}
