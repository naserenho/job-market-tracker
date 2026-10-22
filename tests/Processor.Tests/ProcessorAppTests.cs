using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Tracker.TestSupport;

namespace Processor.Tests;

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

// The whole program, minus finding the repo root and the connection string. Never touches the network or the Archiver.
[Collection("postgres")]
[Trait("Category", "Integration")]
public sealed class ProcessorAppTests(PostgresFixture pg) : IDisposable
{
    private readonly TempDir _root = new();
    private readonly StringWriter _error = new();

    // A copy of the committed config (with the region files) and the example profile, and the fixture archive as its data.
    // The copy names Toronto, the fixture archive's region, whichever region the repo's sources.json names.
    private string Root(bool withProfile = true)
    {
        foreach (var file in new[] { "sources.json", "processor.json", "skills.json" })
            File.Copy(RepoConfig.PathOf(file), _root.File($"config/{file}", ""), overwrite: true);
        SetRegion(_root.Path, "toronto");
        foreach (var file in Directory.GetFiles(RepoConfig.PathOf("regions"), "*.json"))
            File.Copy(file, _root.File($"config/regions/{Path.GetFileName(file)}", ""), overwrite: true);
        if (withProfile) File.Copy(RepoConfig.PathOf("profile.example.json"), _root.File("config/profile.json", ""), overwrite: true);
        FixtureArchive.Write(Path.Combine(_root.Path, "data"));
        return _root.Path;
    }

    private static void SetRegion(string root, string region)
    {
        var sources = Path.Combine(root, "config", "sources.json");
        File.WriteAllText(sources, Regex.Replace(File.ReadAllText(sources), "\"Region\":\\s*\"[^\"]*\"", $"\"Region\": \"{region}\""));
    }

    private async Task<string> FreshDatabaseAsync()
    {
        var name = "processor_" + Guid.NewGuid().ToString("N")[..12];
        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
        await create.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name }.ConnectionString;
    }

    private Task<int> Run(string? root, string? connection, params string[] args) =>
        ProcessorApp.RunAsync(args, root, connection, NullLoggerFactory.Instance, _error, CancellationToken.None);

    [Fact]
    public async Task An_unknown_argument_is_a_config_error_with_usage()
    {
        Assert.Equal(ProcessorApp.ConfigInvalid, await Run(Root(), pg.ConnectionString, "--fast"));
        Assert.Contains("Usage: Processor [--report-only]", _error.ToString());
    }

    [Fact]
    public async Task No_repo_root_is_a_config_error() => Assert.Equal(ProcessorApp.ConfigInvalid, await Run(null, pg.ConnectionString));

    [Fact]
    public async Task A_missing_profile_names_the_example()
    {
        Assert.Equal(ProcessorApp.ConfigInvalid, await Run(Root(withProfile: false), pg.ConnectionString));
        Assert.Contains("profile.example.json", _error.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task No_connection_string_is_a_config_error(string? connection)
    {
        Assert.Equal(ProcessorApp.ConfigInvalid, await Run(Root(), connection));
        Assert.Contains("ConnectionStrings:Tracker", _error.ToString());
    }

    [Fact]
    public async Task An_unreachable_database_exits_3_with_the_docker_hint()
    {
        Assert.Equal(ProcessorApp.DatabaseUnavailable, await Run(Root(), "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2"));
        Assert.Contains("docker compose up", _error.ToString());
    }

    // The server answered, so this is not an outage: the "start Docker" hint would mislead.
    [Fact]
    public async Task A_database_that_refuses_the_migration_exits_1_not_3()
    {
        var connection = await FreshDatabaseAsync();
        await using (var conn = new NpgsqlConnection(connection))
        {
            await conn.OpenAsync();
            await using var clash = new NpgsqlCommand("CREATE TABLE runs (x int)", conn);
            await clash.ExecuteNonQueryAsync();
        }

        Assert.Equal(ProcessorApp.Failed, await Run(Root(), connection));
        Assert.Contains("Database error", _error.ToString());
        Assert.DoesNotContain("docker compose", _error.ToString());
    }

    [Fact]
    public async Task Report_only_on_an_empty_database_says_to_rebuild_first()
    {
        Assert.Equal(ProcessorApp.Failed, await Run(Root(), await FreshDatabaseAsync(), "--report-only"));
        Assert.Contains("without --report-only", _error.ToString());
    }

    [Fact]
    public async Task A_rebuild_then_report_only_both_succeed_and_write_the_report()
    {
        var root = Root();
        var connection = await FreshDatabaseAsync();
        var latest = Path.Combine(root, "data", "reports", "latest.html");

        Assert.Equal(ProcessorApp.Ok, await Run(root, connection));
        Assert.True(File.Exists(latest));
        File.Delete(latest);
        Assert.Equal(ProcessorApp.Ok, await Run(root, connection, "--report-only"));
        Assert.True(File.Exists(latest));
        Assert.Equal("", _error.ToString());
    }

    [Fact]
    public async Task The_ontario_report_has_the_area_section_and_toronto_does_not()
    {
        var root = Root();
        var latest = Path.Combine(root, "data", "reports", "latest.html");

        Assert.Equal(ProcessorApp.Ok, await Run(root, await FreshDatabaseAsync()));
        Assert.DoesNotContain("id=\"areas\"", File.ReadAllText(latest));

        // The same archive collected for Ontario.
        SetRegion(root, "ontario");
        foreach (var manifest in Directory.GetFiles(Path.Combine(root, "data", "raw"), "manifest.json", SearchOption.AllDirectories))
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("{\"runId\"", "{\"region\":\"ontario\",\"runId\""));

        Assert.Equal(ProcessorApp.Ok, await Run(root, await FreshDatabaseAsync()));
        Assert.Contains("id=\"areas\"", File.ReadAllText(latest));
        Assert.Equal("", _error.ToString());
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task An_unknown_region_is_a_config_error_listing_the_available_ones()
    {
        var root = Root();
        SetRegion(root, "atlantis");

        Assert.Equal(ProcessorApp.ConfigInvalid, await Run(root, pg.ConnectionString));
        Assert.Contains("atlantis", _error.ToString());
        Assert.Contains("toronto", _error.ToString());
    }
}
