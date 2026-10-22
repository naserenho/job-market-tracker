using System.Text.Json;
using Tracker.Configuration;

namespace Tracker.Reading;

// What a reader needs besides the JSON: the run, and the region (country code, currency, company display names).
public sealed record SourceContext(string RunId, Region Region);

// One method per source: raw JSON (as archived by phase 0) → RawPosting. Postings missing an id or title are skipped.
public static class SourceReaders
{
    public static IEnumerable<RawPosting> Adzuna(JsonElement root, SourceContext ctx)
    {
        foreach (var r in root.Arr("results"))
        {
            var id = r.Str("id");
            var title = r.Str("title");
            if (id is null || title is null) continue;

            var location = r.Obj("location");
            var area = string.Join(", ", (location?.Arr("area") ?? []).Select(a => a.GetString()).Where(a => !string.IsNullOrEmpty(a)));
            var where = string.Join("; ", new[] { location?.Str("display_name"), area }.Where(s => !string.IsNullOrEmpty(s)));

            SalaryRange? salary = null;
            if (r.Num("salary_min") is { } min && r.Str("salary_is_predicted") != "1")
                salary = new SalaryRange(min, r.Num("salary_max") ?? min, SalaryPeriod.Year);

            // Adzuna searches one country's market, so every result is in the region's country.
            yield return new RawPosting("adzuna", id, ctx.RunId, r.Obj("company")?.Str("display_name") ?? "Unknown",
                HtmlText.ToPlain(title), r.Str("redirect_url") ?? "", where, Remote: null, Country: ctx.Region.CountryCode,
                HtmlText.ToPlain(r.Str("description")), IsFullText: false, salary);
        }
    }

    public static IEnumerable<RawPosting> Greenhouse(JsonElement root, string slug, SourceContext ctx)
    {
        foreach (var j in root.Arr("jobs"))
        {
            var id = j.Str("id");
            var title = j.Str("title");
            if (id is null || title is null) continue;

            yield return new RawPosting("greenhouse", id, ctx.RunId, j.Str("company_name") ?? ctx.Region.CompanyName(slug), title.Trim(),
                j.Str("absolute_url") ?? "", j.Obj("location")?.Str("name") ?? "", Remote: null, Country: null,
                HtmlText.ToPlain(j.Str("content")), IsFullText: true, Salary: null);
        }
    }

    public static IEnumerable<RawPosting> Lever(JsonElement root, string slug, SourceContext ctx)
    {
        if (root.ValueKind != JsonValueKind.Array) yield break;
        foreach (var j in root.EnumerateArray())
        {
            var id = j.Str("id");
            var title = j.Str("text");
            if (id is null || title is null) continue;

            var categories = j.Obj("categories");
            var all = (categories?.Arr("allLocations") ?? []).Select(l => l.GetString()).Where(l => !string.IsNullOrEmpty(l)).ToList();
            var location = all.Count > 0 ? string.Join("; ", all) : categories?.Str("location") ?? "";
            var workplace = j.Str("workplaceType");

            var parts = new List<string?> { j.Str("descriptionPlain") };
            parts.AddRange(j.Arr("lists").Select(l => $"{l.Str("text")}\n{HtmlText.ToPlain(l.Str("content"))}"));
            parts.Add(j.Str("additionalPlain"));

            SalaryRange? salary = null;
            if (j.Obj("salaryRange") is { } range && range.Str("currency") == ctx.Region.Salary.Currency && range.Num("min") is { } min && range.Num("max") is { } max)
                salary = new SalaryRange(min, max, Period(range.Str("interval"), ctx.Region.Salary));

            yield return new RawPosting("lever", id, ctx.RunId, ctx.Region.CompanyName(slug), title.Trim(), j.Str("hostedUrl") ?? "", location,
                Remote: workplace is null ? null : workplace.Equals("remote", StringComparison.OrdinalIgnoreCase),
                Country: j.Str("country"), string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p))), IsFullText: true, salary);
        }
    }

    public static IEnumerable<RawPosting> Ashby(JsonElement root, string slug, SourceContext ctx)
    {
        foreach (var j in root.Arr("jobs"))
        {
            if (j.Bool("isListed") == false) continue;
            var id = j.Str("id");
            var title = j.Str("title");
            if (id is null || title is null) continue;

            var locations = new List<string?> { j.Str("location") };
            locations.AddRange(j.Arr("secondaryLocations").Select(s => s.Str("location")));
            var workplace = j.Str("workplaceType");
            var country = j.Obj("address")?.Obj("postalAddress")?.Str("addressCountry");

            yield return new RawPosting("ashby", id, ctx.RunId, ctx.Region.CompanyName(slug), title.Trim(), j.Str("jobUrl") ?? "",
                string.Join("; ", locations.Where(l => !string.IsNullOrEmpty(l))),
                Remote: workplace is null ? j.Bool("isRemote") : workplace.Equals("Remote", StringComparison.OrdinalIgnoreCase),
                Country: string.IsNullOrEmpty(country) ? null : ctx.Region.CountryNames.Contains(country, StringComparer.Ordinal) ? ctx.Region.CountryCode : country,
                j.Str("descriptionPlain") ?? HtmlText.ToPlain(j.Str("descriptionHtml")), IsFullText: true, AshbySalary(j, ctx.Region.Salary));
        }
    }

    // Workable's public careers-page feed: one object per company, jobs with their locations and HTML descriptions.
    public static IEnumerable<RawPosting> Workable(JsonElement root, string slug, SourceContext ctx)
    {
        var company = root.Str("name") ?? ctx.Region.CompanyName(slug);
        foreach (var j in root.Arr("jobs"))
        {
            var id = j.Str("shortcode");
            var title = j.Str("title");
            if (id is null || title is null) continue;

            static string Place(params string?[] parts) => string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));
            var locations = j.Arr("locations").Select(l => Place(l.Str("city"), l.Str("region"), l.Str("country"))).Where(l => l.Length > 0).ToList();
            if (locations.Count == 0) locations.Add(Place(j.Str("city"), j.Str("state"), j.Str("country")));
            var first = j.Arr("locations").FirstOrDefault();
            var country = first.ValueKind == JsonValueKind.Object ? first.Str("countryCode") ?? first.Str("country") : j.Str("country");

            yield return new RawPosting("workable", id, ctx.RunId, company, title.Trim(), j.Str("url") ?? "",
                string.Join("; ", locations.Where(l => l.Length > 0)),
                Remote: j.Bool("telecommuting"),
                Country: string.IsNullOrEmpty(country) ? null : ctx.Region.CountryNames.Contains(country, StringComparer.Ordinal) ? ctx.Region.CountryCode : country,
                HtmlText.ToPlain(j.Str("description")), IsFullText: true, Salary: null);
        }
    }

    private static readonly string[] SmartRecruitersSections = ["companyDescription", "jobDescription", "qualifications", "additionalInformation"];

    // SmartRecruiters' public posting API: the list has no text, so each posting is joined with its saved detail (null
    // when it wasn't fetched, and then the posting counts as a snippet without text).
    public static IEnumerable<RawPosting> SmartRecruiters(JsonElement list, string slug, Func<string, JsonElement?> detail, SourceContext ctx)
    {
        foreach (var j in list.Arr("content"))
        {
            var id = j.Str("id");
            var title = j.Str("name");
            if (id is null || title is null) continue;

            var location = j.Obj("location");
            var place = string.Join(", ", (location?.Str("fullLocation") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            if (place.Length == 0) place = string.Join(", ", new[] { location?.Str("city"), location?.Str("region") }.Where(p => !string.IsNullOrEmpty(p)));
            var country = location?.Str("country")?.ToUpperInvariant();

            var d = detail(id);
            var sections = d?.Obj("jobAd")?.Obj("sections");
            var text = sections is { } s
                ? string.Join("\n", SmartRecruitersSections.Select(k => HtmlText.ToPlain(s.Obj(k)?.Str("text"))).Where(t => t.Length > 0))
                : "";
            SalaryRange? salary = null;
            if (d?.Obj("compensation") is { } pay && pay.Str("currency") == ctx.Region.Salary.Currency && pay.Num("min") is { } min && pay.Num("max") is { } max)
                salary = new SalaryRange(min, max, Period(pay.Str("period"), ctx.Region.Salary));

            yield return new RawPosting("smartrecruiters", id, ctx.RunId, j.Obj("company")?.Str("name") ?? ctx.Region.CompanyName(slug), title.Trim(),
                d?.Str("postingUrl") ?? $"https://jobs.smartrecruiters.com/{slug}/{id}", place,
                Remote: location?.Bool("remote"),
                Country: string.IsNullOrEmpty(country) ? null : ctx.Region.CountryNames.Contains(country, StringComparer.Ordinal) ? ctx.Region.CountryCode : country,
                text, IsFullText: d is not null, salary);
        }
    }

    private static SalaryRange? AshbySalary(JsonElement job, RegionSalary region)
    {
        var components = (job.Obj("compensation")?.Arr("compensationTiers") ?? []).SelectMany(t => t.Arr("components"));
        foreach (var c in components)
        {
            if (c.Str("compensationType") != "Salary" || c.Str("currencyCode") != region.Currency) continue;
            if (c.Num("minValue") is not { } min || c.Num("maxValue") is not { } max) continue;
            return new SalaryRange(min, max, Period(c.Str("interval"), region));
        }
        return null;
    }

    // A structured pay interval: hourly, monthly where the region pays monthly (the UAE), otherwise yearly.
    private static SalaryPeriod Period(string? interval, RegionSalary region) =>
        interval?.Contains("hour", StringComparison.OrdinalIgnoreCase) == true ? SalaryPeriod.Hour
        : region.MonthlyIsSalary && interval?.Contains("month", StringComparison.OrdinalIgnoreCase) == true ? SalaryPeriod.Month
        : SalaryPeriod.Year;
}
