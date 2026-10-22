using System.Text.Json;
using Api.Applications;

namespace Api.Cv;

public sealed record CvStatusView(bool Ready, IReadOnlyList<string> Problems);

public sealed record GenerateRequest(string? Headline, string? Summary, IReadOnlyList<string?>? BulletIds);

public sealed record GeneratedCv(Guid DocumentId, string FileName);

// Input rules for generating: the same trimming, limits and 400-with-field as every tracker write.
public static class CvRules
{
    public const int MaxSummary = 2000, MaxChosen = 300;

    public static CvChoices Check(GenerateRequest body, MasterCv cv)
    {
        var headline = Rules.NoNul(Rules.SingleLine(Rules.Required(body.Headline, "headline")), "headline");
        var summary = Rules.NoNul(Rules.Optional(body.Summary, "summary", MaxSummary), "summary");
        var ids = (body.BulletIds ?? []).Select(i => i ?? "").ToList();
        if (ids.Count > MaxChosen) throw new TrackerInputException("bulletIds", $"Choose up to {MaxChosen} bullets.");
        var known = cv.Bullets.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.FirstOrDefault(i => !known.Contains(i)) is { } unknown)
            throw new TrackerInputException("bulletIds", $"Bullet '{unknown}' isn't in config/cv.json any more. Reload the page to see your current CV.");
        if (ids.GroupBy(i => i, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new TrackerInputException("bulletIds", $"Bullet '{twice.Key}' is chosen twice.");
        return new CvChoices(headline, summary, ids);
    }
}

// The choices stored with a generated document. Unreadable JSON counts as no saved choices.
public static class CvChoicesJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Write(CvChoices choices) => JsonSerializer.Serialize(choices, Json);

    public static CvChoices? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var choices = JsonSerializer.Deserialize<CvChoices>(json, Json);
            return choices is { Headline: not null, Summary: not null, BulletIds: not null } ? choices : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
