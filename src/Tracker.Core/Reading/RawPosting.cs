namespace Tracker.Reading;

public enum SalaryPeriod { Year, Hour, Month }

public sealed record SalaryRange(decimal Min, decimal Max, SalaryPeriod Period)
{
    public const decimal HoursPerYear = 2080;

    public decimal YearlyMin => Yearly(Min);
    public decimal YearlyMax => Yearly(Max);
    public decimal YearlyMidpoint => (YearlyMin + YearlyMax) / 2;

    // Guards against parse errors and non-salary amounts (stipends, bonuses). The bounds come from the region.
    public bool IsPlausibleWithin(decimal yearlyMin, decimal yearlyMax) => Min <= Max && YearlyMin >= yearlyMin && YearlyMax <= yearlyMax;

    private decimal Yearly(decimal amount) => Period switch
    {
        SalaryPeriod.Hour => amount * HoursPerYear,
        SalaryPeriod.Month => amount * 12,
        _ => amount,
    };
}

// One posting exactly as one source described it in one run. Description is plain text.
public sealed record RawPosting(
    string Source,
    string SourceId,
    string RunId,
    string Company,
    string Title,
    string Url,
    string Location,
    bool? Remote,
    string? Country,
    string Description,
    bool IsFullText,
    SalaryRange? Salary)
{
    public string Key => $"{Source}:{SourceId}";
}
