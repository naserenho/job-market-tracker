using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Tracker.Configuration;

namespace Tracker.Reading;

public sealed record JobBankMonth(DateOnly Month, string NocCode, string NocName, int Postings, int Vacancies, decimal? WageMinMedian, decimal? WageMaxMedian);

// Tech postings per economic region in one month, for comparing a province's cities. Names are kept as Job Bank writes
// them ("Kitchener–Waterloo–Barrie Region", "NA").
public sealed record JobBankRegionMonth(DateOnly Month, string EconomicRegion, int Postings, int Vacancies);

public sealed record JobBankSummary(IReadOnlyList<JobBankMonth> Months, IReadOnlyList<JobBankRegionMonth> Regions)
{
    public static readonly JobBankSummary Empty = new([], []);
}

// Job Bank's monthly open-data file: UTF-16 tab-separated, one row per posting, gzipped by the archiver.
public static partial class JobBankReader
{
    // A region without Job Bank (outside Canada) has no options: nothing to read.
    public static JobBankSummary SummarizeDirectory(string jobBankDir, JobBankOptions? options, ICollection<string> warnings)
    {
        if (options is null) return JobBankSummary.Empty;
        if (!Directory.Exists(jobBankDir)) return JobBankSummary.Empty;
        var months = new List<JobBankMonth>();
        var regions = new List<JobBankRegionMonth>();
        foreach (var file in Directory.EnumerateFiles(jobBankDir).Where(f => f.EndsWith(".csv.gz", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if (!DateOnly.TryParseExact(name[..Math.Min(7, name.Length)], "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) continue;
            try
            {
                using var gz = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
                using var reader = new StreamReader(gz, Encoding.Unicode, detectEncodingFromByteOrderMarks: true);
                var summary = Summarize(reader, month, options);
                months.AddRange(summary.Months);
                regions.AddRange(summary.Regions);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                // A damaged or reshaped monthly file must not block every future rebuild.
                warnings.Add($"Skipped unreadable Job Bank file {name}: {ex.Message}");
            }
        }
        return new JobBankSummary(months, regions);
    }

    // Rows for the configured occupations in the economic region, or in the whole province when Province is set.
    public static JobBankSummary Summarize(TextReader reader, DateOnly month, JobBankOptions options)
    {
        var header = reader.ReadLine()?.Split('\t').Select(h => Spaces().Replace(h.Trim(), " ")).ToList() ?? [];
        int Col(string name) => header.IndexOf(name);
        var (noc, region, vacancies, per, min, max) = (Col("NOC21 Code"), Col("Economic Region"), Col("Vacancy Count"), Col("Salary Per"), Col("Salary Minimum"), Col("Salary Maximum"));
        if (noc < 0 || region < 0) throw new InvalidDataException("Job Bank file has no 'NOC21 Code' or 'Economic Region' column.");
        var byProvince = !string.IsNullOrWhiteSpace(options.Province);
        var province = Col("Province/Territory");
        if (byProvince && province < 0) throw new InvalidDataException("Job Bank file has no 'Province/Territory' column.");
        var (filterColumn, filterValue) = byProvince ? (province, options.Province) : (region, options.EconomicRegion);

        var groups = new Dictionary<string, (int Postings, int Vacancies, List<decimal> Mins, List<decimal> Maxes)>(StringComparer.Ordinal);
        var regions = new Dictionary<string, (int Postings, int Vacancies)>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split('\t');
            string Cell(int i) => i >= 0 && i < cells.Length ? cells[i].Trim() : "";
            var code = Cell(noc);
            if (!options.NocCodes.ContainsKey(code) || !Cell(filterColumn).Equals(filterValue, StringComparison.OrdinalIgnoreCase)) continue;

            (int Postings, int Vacancies, List<decimal> Mins, List<decimal> Maxes) g =
                groups.TryGetValue(code, out var existing) ? existing : (0, 0, new List<decimal>(), new List<decimal>());
            var count = int.TryParse(Cell(vacancies), out var v) && v > 0 ? v : 1;
            g.Postings++;
            g.Vacancies += count;
            var factor = Cell(per).ToLowerInvariant() switch { "hour" => SalaryRange.HoursPerYear, "week" => 52m, "month" => 12m, "year" => 1m, _ => 0m };
            if (factor > 0 && TryMoney(Cell(min), out var lo) && TryMoney(Cell(max), out var hi))
            {
                g.Mins.Add(lo * factor);
                g.Maxes.Add(hi * factor);
            }
            groups[code] = g;

            var r = regions.GetValueOrDefault(Cell(region));
            regions[Cell(region)] = (r.Postings + 1, r.Vacancies + count);
        }

        return new JobBankSummary(
            [.. groups.Select(kv => new JobBankMonth(month, kv.Key, options.NocCodes[kv.Key], kv.Value.Postings, kv.Value.Vacancies,
                Stats.Median(kv.Value.Mins), Stats.Median(kv.Value.Maxes)))],
            [.. regions.Select(kv => new JobBankRegionMonth(month, kv.Key, kv.Value.Postings, kv.Value.Vacancies))]);
    }

    private static bool TryMoney(string value, out decimal amount) =>
        decimal.TryParse(value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
