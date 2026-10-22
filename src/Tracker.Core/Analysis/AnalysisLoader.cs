using Microsoft.EntityFrameworkCore;
using Tracker.Configuration;
using Tracker.Data;
using Tracker.Interpreting;
using Tracker.Reading;

namespace Tracker.Analysis;

public static class AnalysisLoader
{
    // In-scope, non-duplicate postings with their skills and sighting times, plus runs and Job Bank rows.
    public static async Task<(IReadOnlyList<AnalysisPosting> Postings, IReadOnlyList<RunInfo> Runs, IReadOnlyList<JobBankMonth> JobBank)> LoadAsync(TrackerDb db, Region region, CancellationToken ct)
    {
        var runs = await db.Runs.AsNoTracking().OrderBy(r => r.StartedAt).Select(r => new RunInfo(r.RunId, r.StartedAt)).ToListAsync(ct);
        var startedAt = runs.ToDictionary(r => r.RunId, r => r.StartedAt, StringComparer.Ordinal);

        var rows = await db.Postings.AsNoTracking()
            .Where(p => p.InScope && p.DuplicateOf == null)
            .Select(p => new
            {
                p.Id, p.Source, p.Company, p.Title, p.Url, p.City, p.IsLocal, p.IsRemoteInCountry, p.RoleFamily, p.Seniority,
                p.SalaryMin, p.SalaryMax, p.SalaryPeriod, p.IsFullText, p.CitizenshipOrClearance,
                Skills = p.Skills.Select(s => s.Skill!.Name).ToList(),
                Runs = p.Sightings.Select(s => s.RunId).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        var postings = rows.Select(r => new AnalysisPosting(
            r.Id, r.Source, r.Company, r.Title, r.Url,
            r.IsLocal ? r.City ?? region.LocalLabel : region.RemoteLabel,
            TitleRules.ParseFamily(r.RoleFamily) ?? RoleFamily.Other,
            Enum.Parse<Seniority>(r.Seniority),
            r.SalaryMin is { } min && r.SalaryMax is { } max && Enum.TryParse<SalaryPeriod>(r.SalaryPeriod, out var period)
                ? new SalaryRange(min, max, period)
                : null,
            r.IsFullText, r.CitizenshipOrClearance,
            r.Skills.ToHashSet(StringComparer.Ordinal),
            [.. r.Runs.Select(id => startedAt[id]).Order()],
            r.IsLocal, r.IsRemoteInCountry, r.City)).ToList();

        var jobBank = await db.JobBankMonths.AsNoTracking()
            .Select(j => new JobBankMonth(j.Month, j.NocCode, j.NocName, j.Postings, j.Vacancies, j.WageMinMedian, j.WageMaxMedian))
            .ToListAsync(ct);

        return (postings, runs, jobBank);
    }

    // Job Bank tech postings per economic region and month, for the report's "Where the jobs are".
    public static async Task<IReadOnlyList<JobBankRegionMonth>> LoadJobBankRegionsAsync(TrackerDb db, CancellationToken ct) =>
        await db.JobBankRegions.AsNoTracking()
            .Select(j => new JobBankRegionMonth(j.Month, j.EconomicRegion, j.Postings, j.Vacancies))
            .ToListAsync(ct);
}
