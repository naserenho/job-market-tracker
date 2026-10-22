using System.Text.Json;
using Archiver.Sources;
using Archiver.Tests.TestSupport;

namespace Archiver.Tests.Sources;

public class JobBankCatalogTests
{
    internal static byte[] Catalog(params (string Id, string Name, string Lang, string Format)[] resources) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            success = true,
            result = new
            {
                resources = resources.Select(r => new { id = r.Id, name = r.Name, format = r.Format, language = new[] { r.Lang }, url = $"https://files.test/{r.Id}.csv" }),
            },
        });

    [Theory]
    [InlineData("August 2026 Job Postings Advertised on Canada's National Job Bank Website", 2026, 8)]
    [InlineData("January 2023 Job Postings", 2023, 1)]
    public void TryParseMonth_reads_the_leading_month_and_year(string name, int year, int month)
    {
        Assert.True(JobBankCatalog.TryParseMonth(name, out var parsed));
        Assert.Equal(new DateOnly(year, month, 1), parsed);
    }

    [Theory]
    [InlineData("Sept 2026 Job Postings")]
    [InlineData("Job Postings")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseMonth_rejects_anything_else(string? name) =>
        Assert.False(JobBankCatalog.TryParseMonth(name, out _));

    [Fact]
    public void ParseEnglishCsvs_keeps_only_english_csv_with_a_month()
    {
        var json = Catalog(
            ("en-aug", "August 2026 Job Postings", "en", "CSV"),
            ("fr-aug", "August 2026 Job Postings", "fr", "CSV"),
            ("en-xls", "July 2026 Job Postings", "en", "XLSX"),
            ("en-odd", "Data dictionary", "en", "CSV"));

        var parsed = JobBankCatalog.ParseEnglishCsvs(json);

        var only = Assert.Single(parsed);
        Assert.Equal(new JobBankResource("en-aug", new DateOnly(2026, 8, 1), "https://files.test/en-aug.csv"), only);
        Assert.Equal("2026-08_en-aug.csv.gz", only.FileName);
    }

    [Fact]
    public void SelectToDownload_takes_newest_months_not_on_disk()
    {
        var all = new[]
        {
            new JobBankResource("a", new DateOnly(2026, 8, 1), "u"),
            new JobBankResource("b", new DateOnly(2026, 7, 1), "u"),
            new JobBankResource("c", new DateOnly(2026, 6, 1), "u"),
            new JobBankResource("d", new DateOnly(2026, 5, 1), "u"),
        };

        var todo = JobBankCatalog.SelectToDownload(all, new HashSet<DateOnly> { new(2026, 7, 1) }, backfillMonths: 3);

        Assert.Equal(new[] { "a", "c" }, todo.Select(r => r.Id));
    }

    [Fact]
    public void MonthsOnDisk_ignores_tmp_and_foreign_files()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "2026-08_a.csv.gz"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "2026-07_b.csv.gz.tmp"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "notes.txt"), "");

        Assert.Equal(new[] { new DateOnly(2026, 8, 1) }, JobBankCatalog.MonthsOnDisk(tmp.Path));
        Assert.Empty(JobBankCatalog.MonthsOnDisk(Path.Combine(tmp.Path, "missing")));
    }
}
