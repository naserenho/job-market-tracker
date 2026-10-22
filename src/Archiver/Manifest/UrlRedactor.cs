using System.Text.RegularExpressions;

namespace Archiver.Manifest;

public static partial class UrlRedactor
{
    private static readonly string[] SecretParams = ["app_id", "app_key"];

    // Drops credential query parameters from a URL before it is stored.
    public static string Redact(string url)
    {
        var q = url.IndexOf('?');
        if (q < 0) return url;
        var kept = url[(q + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !SecretParams.Contains(p.Split('=')[0], StringComparer.OrdinalIgnoreCase));
        var query = string.Join('&', kept);
        return query.Length == 0 ? url[..q] : $"{url[..q]}?{query}";
    }

    // Masks credential values that appear anywhere in free text, e.g. exception messages.
    public static string ScrubText(string text) => SecretPattern().Replace(text, "$1***");

    [GeneratedRegex(@"(app_(?:id|key)=)[^&\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();
}
