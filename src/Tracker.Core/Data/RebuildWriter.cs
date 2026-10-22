using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Tracker.Interpreting;
using Tracker.Reading;

namespace Tracker.Data;

public sealed record RebuildData(
    IReadOnlyList<ArchiveRun> Runs,
    IReadOnlyList<SkillDefinition> Skills,
    IReadOnlyList<Posting> Postings,
    IReadOnlyList<AdzunaAggregate> Aggregates,
    IReadOnlyList<JobBankMonth> JobBank,
    IReadOnlyList<JobBankRegionMonth>? JobBankRegions = null);

// Replaces all rows in one transaction: nobody sees a half-built database, and a failure leaves the
// previous rebuild in place. Bulk rows use Postgres binary COPY on the transaction's connection.
public sealed class RebuildWriter(TrackerDb db)
{
    private const string Truncate = "TRUNCATE " + TrackerTables.All;

    public async Task WriteAsync(RebuildData data, CancellationToken ct)
    {
        var skillIds = data.Skills.Select((s, i) => (s.Name, Id: i + 1)).ToDictionary(x => x.Name, x => x.Id, StringComparer.Ordinal);
        var runIds = data.Runs.Select(r => r.RunId).ToHashSet(StringComparer.Ordinal);

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using (var truncate = new NpgsqlCommand(Truncate, conn))
                await truncate.ExecuteNonQueryAsync(ct);

            await CopyAsync(conn, "COPY runs (run_id, started_at) FROM STDIN (FORMAT BINARY)", data.Runs, async (w, r) =>
            {
                await Text(w, r.RunId, ct);
                await w.WriteAsync(r.StartedAt.ToUniversalTime(), NpgsqlDbType.TimestampTz, ct);
            }, ct);

            await CopyAsync(conn, "COPY skills (id, name, category) FROM STDIN (FORMAT BINARY)", data.Skills, async (w, s) =>
            {
                await w.WriteAsync(skillIds[s.Name], NpgsqlDbType.Integer, ct);
                await Text(w, s.Name, ct);
                await Text(w, s.Category, ct);
            }, ct);

            await CopyAsync(conn,
                "COPY postings (id, source, source_id, company, title, url, location_text, city, is_local, is_remote_in_country, in_scope, role_family, seniority, salary_min, salary_max, salary_period, salary_origin, description, is_full_text, citizenship_or_clearance, first_seen_run, last_seen_run, duplicate_of) FROM STDIN (FORMAT BINARY)",
                data.Postings, async (w, p) =>
                {
                    await Text(w, p.Id, ct);
                    await Text(w, p.Source, ct);
                    await Text(w, p.SourceId, ct);
                    await Text(w, p.Company, ct);
                    await Text(w, p.Title, ct);
                    await Text(w, p.Url, ct);
                    await Text(w, p.LocationText, ct);
                    await Text(w, p.Scope.City, ct);
                    await w.WriteAsync(p.Scope.IsLocal, NpgsqlDbType.Boolean, ct);
                    await w.WriteAsync(p.Scope.IsRemoteInCountry, NpgsqlDbType.Boolean, ct);
                    await w.WriteAsync(p.Scope.InScope, NpgsqlDbType.Boolean, ct);
                    await Text(w, TitleRules.FamilyKey(p.Family), ct);
                    await Text(w, p.Seniority.ToString(), ct);
                    await Numeric(w, p.Salary?.Min, ct);
                    await Numeric(w, p.Salary?.Max, ct);
                    await Text(w, p.Salary?.Period.ToString(), ct);
                    await Text(w, p.SalaryOrigin, ct);
                    await Text(w, p.Description, ct);
                    await w.WriteAsync(p.IsFullText, NpgsqlDbType.Boolean, ct);
                    await w.WriteAsync(p.CitizenshipOrClearance, NpgsqlDbType.Boolean, ct);
                    await Text(w, p.FirstSeenRun, ct);
                    await Text(w, p.LastSeenRun, ct);
                    await Text(w, p.DuplicateOf, ct);
                }, ct);

            var sightings = data.Postings.SelectMany(p => p.SightingRuns.Select(run =>
                runIds.Contains(run) ? (p.Id, run) : throw new KeyNotFoundException($"Posting {p.Id} was seen in unknown run {run}.")));
            await CopyAsync(conn, "COPY posting_sightings (posting_id, run_id) FROM STDIN (FORMAT BINARY)", sightings, async (w, s) =>
            {
                await Text(w, s.Id, ct);
                await Text(w, s.run, ct);
            }, ct);

            var postingSkills = data.Postings.SelectMany(p => p.Skills.Select(name =>
                skillIds.TryGetValue(name, out var id) ? (p.Id, id) : throw new KeyNotFoundException($"Posting {p.Id} has unknown skill '{name}'.")));
            await CopyAsync(conn, "COPY posting_skills (posting_id, skill_id) FROM STDIN (FORMAT BINARY)", postingSkills, async (w, s) =>
            {
                await Text(w, s.Id, ct);
                await w.WriteAsync(s.id, NpgsqlDbType.Integer, ct);
            }, ct);

            await CopyAsync(conn, "COPY adzuna_aggregates (run_id, term, kind, payload) FROM STDIN (FORMAT BINARY)", data.Aggregates, async (w, a) =>
            {
                await Text(w, a.RunId, ct);
                await Text(w, a.Term, ct);
                await Text(w, a.Kind, ct);
                await w.WriteAsync(a.PayloadJson, NpgsqlDbType.Jsonb, ct);
            }, ct);

            await CopyAsync(conn, "COPY jobbank_monthly (month, noc_code, noc_name, postings, vacancies, wage_min_median, wage_max_median) FROM STDIN (FORMAT BINARY)", data.JobBank, async (w, j) =>
            {
                await w.WriteAsync(j.Month, NpgsqlDbType.Date, ct);
                await Text(w, j.NocCode, ct);
                await Text(w, j.NocName, ct);
                await w.WriteAsync(j.Postings, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(j.Vacancies, NpgsqlDbType.Integer, ct);
                await Numeric(w, j.WageMinMedian, ct);
                await Numeric(w, j.WageMaxMedian, ct);
            }, ct);

            await CopyAsync(conn, "COPY jobbank_regions (month, economic_region, postings, vacancies) FROM STDIN (FORMAT BINARY)", data.JobBankRegions ?? [], async (w, j) =>
            {
                await w.WriteAsync(j.Month, NpgsqlDbType.Date, ct);
                await Text(w, j.EconomicRegion, ct);
                await w.WriteAsync(j.Postings, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(j.Vacancies, NpgsqlDbType.Integer, ct);
            }, ct);

            await tx.CommitAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task CopyAsync<T>(NpgsqlConnection conn, string copy, IEnumerable<T> rows, Func<NpgsqlBinaryImporter, T, Task> writeRow, CancellationToken ct)
    {
        await using var importer = await conn.BeginBinaryImportAsync(copy, ct);
        foreach (var row in rows)
        {
            await importer.StartRowAsync(ct);
            await writeRow(importer, row);
        }
        await importer.CompleteAsync(ct);
    }

    // Postgres text cannot hold NUL characters, which occasionally appear in scraped descriptions.
    private static Task Text(NpgsqlBinaryImporter w, string? value, CancellationToken ct) =>
        value is null ? w.WriteNullAsync(ct) : w.WriteAsync(value.Replace("\0", ""), NpgsqlDbType.Text, ct);

    private static Task Numeric(NpgsqlBinaryImporter w, decimal? value, CancellationToken ct) =>
        value is { } d ? w.WriteAsync(d, NpgsqlDbType.Numeric, ct) : w.WriteNullAsync(ct);
}
