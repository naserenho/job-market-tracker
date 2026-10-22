using Archiver.Configuration;

namespace Archiver.Sources;

public enum AdzunaKind { Histogram, TopCompanies, Search }

public sealed record AdzunaRequest(AdzunaKind Kind, string Term, string Scope, int Page);

public static class AdzunaQueryPlan
{
    // "gta" is the local search's tag in file names in every region; kept so old archives read unchanged.
    public const string Gta = "gta";
    public const string Remote = "remote";

    // Aggregates first so the budget can never cut them; searches page-major so every query gets page 1 first.
    public static IReadOnlyList<AdzunaRequest> Build(AdzunaOptions o)
    {
        var plan = new List<AdzunaRequest>();
        plan.AddRange(o.Terms.Select(t => new AdzunaRequest(AdzunaKind.Histogram, t, Gta, 0)));
        plan.AddRange(o.Terms.Select(t => new AdzunaRequest(AdzunaKind.TopCompanies, t, Gta, 0)));
        for (var page = 1; page <= o.MaxPages; page++)
            foreach (var term in o.Terms)
                foreach (var scope in new[] { Gta, Remote })
                    plan.Add(new AdzunaRequest(AdzunaKind.Search, term, scope, page));
        return plan;
    }

    public static string Url(AdzunaOptions o, AdzunaRequest r)
    {
        var what = r.Scope == Remote ? $"{r.Term} remote" : r.Term;
        var query = new List<string>
        {
            $"app_id={E(o.AppId)}",
            $"app_key={E(o.AppKey)}",
            $"what={E(what)}",
            "content-type=application/json",
        };
        if (r.Scope == Gta) query.Add($"where={E(o.Where)}");

        string path;
        switch (r.Kind)
        {
            case AdzunaKind.Search:
                path = $"search/{r.Page}";
                query.Add($"results_per_page={o.ResultsPerPage}");
                // No distance (0): the place is the whole search area, such as a province.
                if (r.Scope == Gta && o.DistanceKm > 0) query.Add($"distance={o.DistanceKm}");
                break;
            case AdzunaKind.Histogram:
                path = "histogram";
                break;
            default:
                path = "top_companies";
                break;
        }
        return $"{o.BaseUrl.TrimEnd('/')}/{o.Country}/{path}?{string.Join('&', query)}";
    }

    private static string E(string? value) => Uri.EscapeDataString(value ?? "");
}
