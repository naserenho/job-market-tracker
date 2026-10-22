using Archiver.Configuration;
using Archiver.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Archiver.Tests.Configuration;

public class RepoConfigTests
{
    private static readonly string Root = RepoRoot.Find(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Repo root not found.");

    // Exactly as Program.cs layers it: sources.json, then the region file it names.
    private static SourcesOptions Bind(string? region = null)
    {
        var path = RegionConfig.Resolve(Root, region, out var error) ?? throw new InvalidOperationException(error);
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(Root, "config", "sources.json"))
            .AddJsonFile(path)
            .Build()
            .Get<SourcesOptions>()!;
    }

    [Fact]
    public void Committed_sources_json_and_its_active_region_bind_and_validate()
    {
        var options = Bind();

        Assert.Empty(SourcesValidator.Validate(options));
        Assert.NotNull(RegionConfig.Resolve(Root, options.Region, out _));
        Assert.Equal(7, options.Adzuna.Terms.Count);
        Assert.Null(options.Adzuna.AppKey); // secrets are never committed
    }

    [Fact]
    public void Toronto_binds_and_validates()
    {
        var options = Bind("toronto");

        Assert.Empty(SourcesValidator.Validate(options));
        Assert.Equal(("ca", "Toronto", 50, true), (options.Adzuna.Country, options.Adzuna.Where, options.Adzuna.DistanceKm, options.Adzuna.Enabled));
        Assert.InRange(options.Boards.Count, 20, 40);
        Assert.NotEmpty(options.JobBank.DatasetId);
    }

    [Fact]
    public void Abu_dhabi_binds_without_adzuna_or_job_bank()
    {
        var options = Bind("abu-dhabi");

        Assert.Empty(SourcesValidator.Validate(options));
        Assert.False(options.Adzuna.Enabled);
        Assert.Equal("", options.JobBank.DatasetId);
        Assert.NotEmpty(options.Boards);
    }

    [Fact]
    public void An_unknown_region_lists_the_available_ones()
    {
        Assert.Null(RegionConfig.Resolve(Root, "atlantis", out var error));
        Assert.Contains("toronto", error);
        Assert.Contains("abu-dhabi", error);
    }

    [Fact]
    public void Find_walks_up_to_the_folder_holding_config()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "config"));
        File.WriteAllText(Path.Combine(tmp.Path, "config", "sources.json"), "{}");
        var deep = Directory.CreateDirectory(Path.Combine(tmp.Path, "src", "Archiver", "bin")).FullName;

        Assert.Equal(new DirectoryInfo(tmp.Path).FullName, RepoRoot.Find(deep));
    }

    [Fact]
    public void Find_returns_null_when_no_config_exists()
    {
        using var tmp = new TempDir();
        Assert.Null(RepoRoot.Find(tmp.Path));
    }
}
