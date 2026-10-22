using System.Globalization;
using System.Text.Json;

namespace Archiver.Sources;

public sealed record JobBankResource(string Id, DateOnly Month, string Url)
{
    public string FileName => $"{Month:yyyy-MM}_{Id}.csv.gz";
}

public static class JobBankCatalog
{
    private const string FileSuffix = ".csv.gz";

    // Resource names look like "August 2026 Job Postings Advertised on Canada's National Job Bank Website".
    public static bool TryParseMonth(string? name, out DateOnly month)
    {
        month = default;
        var parts = (name ?? "").Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        if (!DateTime.TryParseExact($"{parts[0]} {parts[1]}", "MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        month = new DateOnly(parsed.Year, parsed.Month, 1);
        return true;
    }

    // Throws JsonException/KeyNotFoundException/InvalidOperationException when the catalog isn't CKAN package_show output.
    public static IReadOnlyList<JobBankResource> ParseEnglishCsvs(byte[] packageShowJson)
    {
        using var doc = JsonDocument.Parse(packageShowJson);
        var list = new List<JobBankResource>();
        foreach (var r in doc.RootElement.GetProperty("result").GetProperty("resources").EnumerateArray())
        {
            if (!string.Equals(Str(r, "format"), "CSV", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsEnglish(r)) continue;
            if (Str(r, "id") is { Length: > 0 } id && Str(r, "url") is { Length: > 0 } url && TryParseMonth(Str(r, "name"), out var month))
                list.Add(new JobBankResource(id, month, url));
        }
        return list;
    }

    public static IReadOnlySet<DateOnly> MonthsOnDisk(string directory)
    {
        var months = new HashSet<DateOnly>();
        if (!Directory.Exists(directory)) return months;
        foreach (var name in Directory.EnumerateFiles(directory).Select(Path.GetFileName))
        {
            if (name is null || !name.EndsWith(FileSuffix, StringComparison.Ordinal) || name.Length < 8) continue;
            if (DateOnly.TryParseExact(name[..7], "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                months.Add(month);
        }
        return months;
    }

    // Keeps the newest `backfillMonths` months on disk; anything missing among them is downloaded.
    public static IReadOnlyList<JobBankResource> SelectToDownload(IEnumerable<JobBankResource> all, IReadOnlySet<DateOnly> monthsOnDisk, int backfillMonths) =>
        all.GroupBy(r => r.Month)
            .Select(g => g.First())
            .OrderByDescending(r => r.Month)
            .Take(backfillMonths)
            .Where(r => !monthsOnDisk.Contains(r.Month))
            .ToList();

    private static string? Str(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool IsEnglish(JsonElement r) =>
        r.TryGetProperty("language", out var lang)
        && lang.ValueKind == JsonValueKind.Array
        && lang.EnumerateArray().Any(l => l.ValueKind == JsonValueKind.String && l.GetString() == "en");
}
