using System.Net;
using System.Text.RegularExpressions;

namespace Tracker.Reading;

public static partial class HtmlText
{
    // Greenhouse double-encodes its HTML (&lt;p&gt;), so decode, strip tags, then decode entities again.
    public static string ToPlain(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = WebUtility.HtmlDecode(html);
        text = BlockTags().Replace(text, "\n");
        text = Tags().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = Spaces().Replace(text, " ");
        text = LineBreaks().Replace(text, "\n");
        return text.Trim();
    }

    [GeneratedRegex(@"<\s*(?:br|/p|/div|/li|/h[1-6]|li)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTags();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex LineBreaks();
}
