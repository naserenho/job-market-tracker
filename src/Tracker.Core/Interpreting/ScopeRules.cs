using System.Text.RegularExpressions;
using Tracker.Configuration;
using Tracker.Reading;

namespace Tracker.Interpreting;

public sealed record Scope(string? City, bool IsLocal, bool IsRemoteInCountry)
{
    public bool InScope => IsLocal || IsRemoteInCountry;
}

// The patterns built from the region file, for tests that pin them.
public sealed record ScopePatterns(string InCountryWords, string InCountryCodes, string Foreign);

// Locations often list several options ("Bellevue, WA; Toronto, ON" or "Remote - US; Vancouver, BC"), so every rule
// works per ";"-separated segment: a local place counts only in a segment that isn't marked as another country, and
// remote-in-country needs one segment that is both remote and in the country (or a structured remote flag plus an
// in-country address). The places, country words and codes come from the region file.
public sealed partial class ScopeRules
{
    private readonly (Regex Pattern, string City, bool NeedsContext)[] _places;
    private readonly string _country;
    private readonly Regex _inCountryWords, _inCountryCodes, _foreign;
    // Only for a region with Local.Anywhere (a whole province): null otherwise.
    private readonly Regex? _anywhereWords, _anywhereCodes, _exclude, _notATown, _foreignWords;
    private readonly string _localLabel;

    public ScopeRules(Region region)
    {
        var ambiguous = new HashSet<string>(region.Local.NeedsCountryContext, StringComparer.OrdinalIgnoreCase);
        _places = [.. region.Local.Places.Select(p => (
            new Regex($@"(?<![\p{{L}}]){Regex.Escape(p.Key)}(?![\p{{L}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            p.Value,
            ambiguous.Contains(p.Key)))];
        _country = region.CountryCode;
        Patterns = new ScopePatterns(
            RegionPatterns.Words(region.InCountry.Words),
            RegionPatterns.Codes("(?:,|-)", region.InCountry.Codes),
            RegionPatterns.WordsOrCodes(region.Foreign.Words, region.Foreign.Codes));
        _inCountryWords = new Regex(Patterns.InCountryWords, RegexOptions.IgnoreCase);
        _inCountryCodes = new Regex(Patterns.InCountryCodes);
        _foreign = new Regex(Patterns.Foreign, RegexOptions.IgnoreCase);
        _localLabel = region.LocalLabel;
        if (region.Local.Anywhere is { } anywhere)
        {
            _anywhereWords = new Regex(RegionPatterns.Words(anywhere.Words), RegexOptions.IgnoreCase);
            // Case-sensitive, like the in-country codes: "Hybrid - on site" is not "- ON". "^" allows "ON, Canada".
            _anywhereCodes = new Regex(RegionPatterns.Codes("(?:^|,|-)", anywhere.Codes));
            _exclude = new Regex(RegionPatterns.Words(region.Local.Exclude ?? []), RegexOptions.IgnoreCase);
            // Another country's name rules a segment out even beside "Ontario" ("Ontario, USA"). Codes don't: ", CA" is
            // also Canada's ("Timmins, ON, CA").
            _foreignWords = new Regex(RegionPatterns.Words(region.Foreign.Words), RegexOptions.IgnoreCase);
            // What a location part can be made of without naming a town: "Ontario", "ON", "Canada", "Remote".
            _notATown = new Regex(string.Join('|',
                RegionPatterns.Words(anywhere.Words),
                RegionPatterns.WordsOrStandaloneCodes([], anywhere.Codes),
                RegionPatterns.Words([.. region.CountryNames.Select(Regex.Escape)]),
                Patterns.InCountryWords,
                @"\bremote\b",
                @"[\s\-–.]"), RegexOptions.IgnoreCase);
        }
    }

    public ScopePatterns Patterns { get; }

    public Scope Classify(RawPosting p)
    {
        var segments = Segments(p.Location);
        var city = LocalCity(p) ?? AnywhereCity(p);
        var allInCountry = segments.Length > 0 && segments.All(IsInCountrySegment);
        var remoteInCountry =
            (p.Remote == true && p.Country == _country)
            || segments.Any(s => RemoteWord().IsMatch(s) && IsInCountrySegment(s))
            || ((p.Remote == true || RemoteWord().IsMatch(p.Title)) && (p.Country is null || p.Country == _country) && allInCountry)
            || (p.Source == "adzuna" && RemoteSnippet().IsMatch(p.Description) && IsInCountry(p));
        return new Scope(city ?? FirstSegment(p.Location), city is not null, remoteInCountry);
    }

    public bool IsInCountry(RawPosting p) => p.Country == _country || Segments(p.Location).Any(IsInCountrySegment);

    private static string[] Segments(string location) =>
        location.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private bool IsInCountrySegment(string segment) => _inCountryWords.IsMatch(segment) || _inCountryCodes.IsMatch(segment);

    // "Oakville, CT", "Scarborough, UK", "Toronto, OH": another country's marker and nothing in-country.
    private bool IsForeignSegment(string segment) => _foreign.IsMatch(segment) && !IsInCountrySegment(segment);

    // The local place that appears earliest in the location text; ambiguous names need in-country context in their segment.
    private string? LocalCity(RawPosting p)
    {
        string? best = null;
        var bestIndex = int.MaxValue;
        foreach (var segment in Segments(p.Location))
        {
            if (p.Country != _country && IsForeignSegment(segment)) continue;
            var offset = p.Location.IndexOf(segment, StringComparison.Ordinal);
            foreach (var (pattern, city, needsContext) in _places)
            {
                var m = pattern.Match(segment);
                if (!m.Success || offset + m.Index >= bestIndex) continue;
                if (needsContext && p.Country != _country && !IsInCountrySegment(segment)) continue;
                best = city;
                bestIndex = offset + m.Index;
            }
        }
        return best;
    }

    // A region covering a whole province: the first segment in it ("Sudbury, ON") is local even when Places doesn't
    // list the town. The town shown is the first part that names one, else the local label ("Ontario, Canada").
    private string? AnywhereCity(RawPosting p)
    {
        if (_anywhereWords is null || _anywhereCodes is null || _exclude is null || _notATown is null || _foreignWords is null) return null;
        foreach (var segment in Segments(p.Location))
        {
            if (p.Country != _country && (IsForeignSegment(segment) || _foreignWords.IsMatch(segment) || _exclude.IsMatch(segment))) continue;
            if (!_anywhereWords.IsMatch(segment) && !_anywhereCodes.IsMatch(segment)) continue;
            var town = segment.Split(',', StringSplitOptions.TrimEntries).FirstOrDefault(part => _notATown.Replace(part, "").Length > 0);
            return town is null ? _localLabel : town.Length > 80 ? town[..80] : town;
        }
        return null;
    }

    private static string? FirstSegment(string location)
    {
        var first = location.Split([';', ','], 2)[0].Trim();
        return first.Length == 0 ? null : first.Length > 80 ? first[..80] : first;
    }

    [GeneratedRegex(@"\bremote\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteWord();

    [GeneratedRegex(@"\b(?:fully remote|100% remote|remote[- ]first|remote position|remote role|remote opportunity|work from home|work-from-home|WFH)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteSnippet();
}
