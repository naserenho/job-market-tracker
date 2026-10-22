using System.Text.RegularExpressions;

namespace Tracker.Interpreting;

// Builds the regular expressions a region file describes, in the exact shape the Toronto rules were written by hand,
// so the Toronto file reproduces them character for character.
internal static class RegionPatterns
{
    // Matches nothing: a region with an empty list never matches it.
    public const string Never = "(?!)";

    // \b(?:a|b)\b over pattern fragments.
    public static string Words(IReadOnlyList<string> patterns) =>
        patterns.Count == 0 ? Never : $@"\b(?:{string.Join('|', patterns)})\b";

    // Literal, case-sensitive codes after one of the separators: "(?:,|-)\s*(?:ON|BC)\b".
    public static string Codes(string separators, IReadOnlyList<string> codes) =>
        codes.Count == 0 ? Never : $@"{separators}\s*(?:{string.Join('|', codes.Select(Regex.Escape))})\b";

    // Words (case-insensitive) or ", CODE" (case-sensitive inside a pattern compiled with IgnoreCase).
    public static string WordsOrCodes(IReadOnlyList<string> words, IReadOnlyList<string> codes)
    {
        var parts = new List<string>();
        if (words.Count > 0) parts.Add($@"\b(?:{string.Join('|', words)})\b");
        if (codes.Count > 0) parts.Add($@",\s*(?-i:{string.Join('|', codes.Select(Regex.Escape))})\b");
        return parts.Count == 0 ? Never : string.Join('|', parts);
    }

    // Words (case-insensitive) or standalone codes (case-sensitive): "\b(?:new york)\b|(?-i:\b(?:US|SF)\b)".
    public static string WordsOrStandaloneCodes(IReadOnlyList<string> words, IReadOnlyList<string> codes)
    {
        var parts = new List<string>();
        if (words.Count > 0) parts.Add($@"\b(?:{string.Join('|', words)})\b");
        if (codes.Count > 0) parts.Add($@"(?-i:\b(?:{string.Join('|', codes.Select(Regex.Escape))})\b)");
        return parts.Count == 0 ? Never : string.Join('|', parts);
    }
}
