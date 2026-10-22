using System.Text.RegularExpressions;
using Tracker.Reading;

namespace Api.Applications;

// A pasted job becomes an inbox file in the same format as a saved LinkedIn post, so the next rebuild adds it to the
// market data as posting "inbox:<file name>".
public static partial class InboxWriter
{
    // Region: the region the job was pasted under, so another region's rebuild skips it.
    public static string Content(string title, string company, string url, string location, string text, DateOnly saved, string region) =>
        $"URL: {Rules.SingleLine(url)}\nCompany: {Rules.SingleLine(company)}\nTitle: {Rules.SingleLine(title)}\n" +
        $"Location: {Rules.SingleLine(location)}\nSaved: {saved:yyyy-MM-dd}\nRegion: {region}\n\n{text.Trim()}\n";

    public static string FileName(string company, string title, DateOnly saved, Func<string, bool> exists)
    {
        var stem = $"{saved:yyyy-MM-dd}-{Slug(company)}-{Slug(title)}";
        var name = stem + ".txt";
        for (var n = 2; exists(name); n++) name = $"{stem}-{n}.txt";
        return name;
    }

    public static string Slug(string value)
    {
        var slug = NonAlphanumeric().Replace(value.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        return slug.Length == 0 ? "x" : slug;
    }

    // Writes the file only when the rebuild's own reader accepts it, and returns the posting id it will get.
    public static (string PostingId, string Path) Write(string inboxFolder, string title, string company, string url,
        string location, string text, DateOnly saved, string region)
    {
        var name = FileName(company, title, saved, candidate => File.Exists(Path.Combine(inboxFolder, candidate)));
        var content = Content(title, company, url, location, text, saved, region);
        var (posting, warning) = InboxReader.Parse(name, content, "check");
        if (posting is null)
            throw new TrackerInputException("postingText", warning ?? "The pasted job can't be saved for the market data.");
        Directory.CreateDirectory(inboxFolder);
        var path = Path.Combine(inboxFolder, name);
        File.WriteAllText(path, content);
        return ("inbox:" + name, path);
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
