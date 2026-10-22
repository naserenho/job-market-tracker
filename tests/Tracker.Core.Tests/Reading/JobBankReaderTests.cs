using System.IO.Compression;
using System.Text;
using Tracker.Configuration;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Reading;

public class JobBankReaderTests
{
    private static readonly JobBankOptions Options = new()
    {
        EconomicRegion = "Toronto Region",
        NocCodes = new() { ["21232"] = "Software developers and programmers", ["21231"] = "Software engineers and designers" },
    };

    // Real header names, including the double space in "Economic  Region".
    private const string Tsv =
        "Job Title\tNOC21 Code\tNOC21 Code Name\tEconomic  Region\tVacancy Count\tSalary Per\tSalary Minimum\tSalary Maximum\n" +
        "Dev A\t21232\tSoftware developers and programmers\tToronto Region\t2\tHour\t40\t50\n" +
        "Dev B\t21232\tSoftware developers and programmers\tToronto Region\t1\tYear\t90000\t110000\n" +
        "Dev C\t21232\tSoftware developers and programmers\tOttawa Region\t1\tYear\t80000\t90000\n" +
        "Cook\t63200\tCooks\tToronto Region\t3\tHour\t17\t18\n" +
        "Eng\t21231\tSoftware engineers and designers\tToronto Region\tNA\tNA\tNA\tNA\n";

    [Fact]
    public void Summarize_counts_configured_nocs_in_the_region_with_yearly_wage_medians()
    {
        var months = JobBankReader.Summarize(new StringReader(Tsv), new DateOnly(2026, 8, 1), Options).Months.OrderBy(m => m.NocCode).ToList();

        Assert.Equal(2, months.Count);
        var engineers = months[0];
        Assert.Equal(("21231", 1, 1), (engineers.NocCode, engineers.Postings, engineers.Vacancies)); // NA vacancy counts as 1
        Assert.Null(engineers.WageMinMedian);

        var developers = months[1];
        Assert.Equal(("21232", 2, 3), (developers.NocCode, developers.Postings, developers.Vacancies));
        Assert.Equal((83_200m + 90_000m) / 2, developers.WageMinMedian); // 40/h × 2080 = 83,200
        Assert.Equal((104_000m + 110_000m) / 2, developers.WageMaxMedian);
    }

    private static readonly JobBankOptions Ontario = new() { Province = "Ontario", NocCodes = Options.NocCodes };

    // Region names keep Job Bank's en dashes; "NA" (no region given) is a row of its own.
    private const string OntarioTsv =
        "Job Title\tNOC21 Code\tNOC21 Code Name\tProvince/Territory\tEconomic  Region\tVacancy Count\tSalary Per\tSalary Minimum\tSalary Maximum\n" +
        "Dev A\t21232\tSoftware developers and programmers\tOntario\tToronto Region\t2\tYear\t90000\t110000\n" +
        "Dev O\t21232\tSoftware developers and programmers\tOntario\tOttawa Region\t1\tYear\t80000\t90000\n" +
        "Dev K\t21231\tSoftware engineers and designers\tOntario\tKitchener–Waterloo–Barrie Region\t3\tNA\tNA\tNA\n" +
        "Dev N\t21232\tSoftware developers and programmers\tOntario\tNA\t1\tNA\tNA\tNA\n" +
        "Dev Q\t21232\tSoftware developers and programmers\tQuébec\tMontréal Region\t1\tYear\t70000\t80000\n" +
        "Cook\t63200\tCooks\tOntario\tOttawa Region\t3\tHour\t17\t18\n";

    [Fact]
    public void Province_filter_counts_every_ontario_region_and_totals_them()
    {
        var summary = JobBankReader.Summarize(new StringReader(OntarioTsv), new DateOnly(2026, 8, 1), Ontario);

        var byNoc = summary.Months.ToDictionary(m => m.NocCode);
        Assert.Equal((3, 4), (byNoc["21232"].Postings, byNoc["21232"].Vacancies));
        Assert.Equal((1, 3), (byNoc["21231"].Postings, byNoc["21231"].Vacancies));
        Assert.Equal(
            [("Kitchener–Waterloo–Barrie Region", 1, 3), ("NA", 1, 1), ("Ottawa Region", 1, 1), ("Toronto Region", 1, 2)],
            summary.Regions.OrderBy(r => r.EconomicRegion, StringComparer.Ordinal).Select(r => (r.EconomicRegion, r.Postings, r.Vacancies)));
        Assert.All(summary.Regions, r => Assert.Equal(new DateOnly(2026, 8, 1), r.Month));
    }

    [Fact]
    public void Economic_region_filter_is_unchanged()
    {
        var summary = JobBankReader.Summarize(new StringReader(Tsv), new DateOnly(2026, 8, 1), Options);

        Assert.Equal(2, summary.Months.Count);
        var region = Assert.Single(summary.Regions);
        Assert.Equal(("Toronto Region", 3, 4), (region.EconomicRegion, region.Postings, region.Vacancies));
    }

    [Fact]
    public void A_province_filter_needs_the_province_column() =>
        Assert.Throws<InvalidDataException>(() => JobBankReader.Summarize(new StringReader(Tsv), new DateOnly(2026, 8, 1), Ontario));

    [Fact]
    public void SummarizeDirectory_reads_gzipped_utf16_files_named_by_month()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "2026-08_abc.csv.gz");
        using (var gz = new GZipStream(File.Create(file), CompressionLevel.Fastest))
        using (var writer = new StreamWriter(gz, Encoding.Unicode))
            writer.Write(Tsv);
        File.WriteAllText(Path.Combine(tmp.Path, "2026-07_x.csv.gz.tmp"), "partial");
        var warnings = new List<string>();

        var months = JobBankReader.SummarizeDirectory(tmp.Path, Options, warnings).Months;

        Assert.All(months, m => Assert.Equal(new DateOnly(2026, 8, 1), m.Month));
        Assert.Equal(2, months.Count);
        Assert.Empty(warnings);
        Assert.Empty(JobBankReader.SummarizeDirectory(Path.Combine(tmp.Path, "missing"), Options, warnings).Months);
    }

    [Fact]
    public void A_corrupt_monthly_file_is_skipped_with_a_warning()
    {
        using var tmp = new TempDir();
        using (var gz = new GZipStream(File.Create(Path.Combine(tmp.Path, "2026-08_good.csv.gz")), CompressionLevel.Fastest))
        using (var writer = new StreamWriter(gz, Encoding.Unicode))
            writer.Write(Tsv);
        File.WriteAllText(Path.Combine(tmp.Path, "2026-07_bad.csv.gz"), "not gzip at all");
        var warnings = new List<string>();

        var months = JobBankReader.SummarizeDirectory(tmp.Path, Options, warnings).Months;

        Assert.Equal(2, months.Count);
        Assert.Contains("2026-07_bad.csv.gz", Assert.Single(warnings));
    }
}
