using Api.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

// A database last used by phase 2b is upgraded in place: its rows stay, and older documents count as sent, not generated.
[Collection("postgres")]
[Trait("Category", "Integration")]
public sealed class TrackerUpgradeTests(PostgresFixture pg)
{
    [Fact]
    public async Task Upgrading_a_2b_database_keeps_its_rows()
    {
        var connection = await FreshDatabaseAsync();
        await using (var db = new ApplicationsDb(ApplicationsDb.Options(connection)))
            await db.GetService<IMigrator>().MigrateAsync("20260929031945_InitialApplications");
        await using (var conn = new NpgsqlConnection(connection))
        {
            await conn.OpenAsync();
            await using var insert = new NpgsqlCommand("""
                INSERT INTO companies (id, name, name_key, website, office_location, company_size, annual_leave, benefits, notes, created_at, updated_at)
                VALUES ('11111111-1111-1111-1111-111111111111', 'RBC', 'rbc', '', '', '', '', '', '', now(), now());
                INSERT INTO applications (id, company_id, posting_id, title, url, location, posting_text, status, origin, found_via, work_model,
                    office_days, team_size, working_hours, salary_posted, salary_asked, salary_offered, notes, created_at, updated_at)
                VALUES ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', NULL, 'Developer', '', '', 'C#',
                    'Applied', 'Pasted', '', 'Unknown', '', '', '', '', '', '', '', now(), now());
                INSERT INTO application_documents (id, application_id, kind, label, file_name, content_type, size_bytes, sent_on, created_at)
                VALUES ('33333333-3333-3333-3333-333333333333', '22222222-2222-2222-2222-222222222222', 'Cv', 'CV v3', 'CV.pdf',
                    'application/pdf', 10, '2027-03-01', now());
                """, conn);
            await insert.ExecuteNonQueryAsync();
        }

        await using var upgraded = new ApplicationsDb(ApplicationsDb.Options(connection));
        await upgraded.Database.MigrateAsync();

        var document = await upgraded.Documents.SingleAsync();
        Assert.Equal(("CV v3", (string?)null), (document.Label, document.CvChoices));
        Assert.Equal("Developer", (await upgraded.Applications.SingleAsync()).Title);
    }

    private async Task<string> FreshDatabaseAsync()
    {
        var name = "upgrade_" + Guid.NewGuid().ToString("N")[..12];
        await using (var conn = new NpgsqlConnection(pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
            await create.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name }.ConnectionString;
    }
}
