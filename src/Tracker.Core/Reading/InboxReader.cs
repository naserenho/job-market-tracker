using System.Globalization;

namespace Tracker.Reading;

public sealed record InboxResult(IReadOnlyList<RawPosting> Postings, IReadOnlyList<string> Warnings);

// data/inbox/*.txt: header lines (URL/Company/Title/Location/Saved/Region), a blank line, then the posting text.
// A pasted job is seen once: in the first run on or after its Saved date, so it ages out of the window like a
// collected posting. Without a readable Saved date it counts as seen in the latest run for as long as the file exists.
// A file whose Region line names another region is skipped; a file without one belongs to the active region.
public static class InboxReader
{
    public static InboxResult ReadAll(string inboxDir, IReadOnlyList<ArchiveRun> runs, string region)
    {
        if (!Directory.Exists(inboxDir)) return new InboxResult([], []);
        var postings = new List<RawPosting>();
        var warnings = new List<string>();
        var otherRegion = 0;
        foreach (var file in Directory.EnumerateFiles(inboxDir, "*.txt").Order(StringComparer.Ordinal))
        {
            var content = File.ReadAllText(file);
            if (RegionOf(content) is { } fileRegion && !fileRegion.Equals(region, StringComparison.OrdinalIgnoreCase))
            {
                otherRegion++;
                continue;
            }
            var (posting, fileWarnings) = Parse(Path.GetFileName(file), content, runs);
            if (posting is not null) postings.Add(posting);
            warnings.AddRange(fileWarnings);
        }
        if (otherRegion > 0)
            warnings.Add(otherRegion == 1
                ? "1 inbox file belongs to another region and was skipped."
                : $"{otherRegion} inbox files belong to another region and were skipped.");
        return new InboxResult(postings, warnings);
    }

    // The Region header line, or null when the file has none.
    public static string? RegionOf(string content) =>
        Headers(content.Replace("\r\n", "\n").Split('\n'), out _).TryGetValue("Region", out var region) && region.Length > 0 ? region : null;

    private static Dictionary<string, string> Headers(string[] lines, out int bodyStart)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        for (; i < lines.Length && lines[i].Trim().Length > 0; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon > 0) headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }
        bodyStart = i;
        return headers;
    }

    // For checking a file before it is written: the run does not matter.
    public static (RawPosting? Posting, string? Warning) Parse(string fileName, string content, string runId)
    {
        var (posting, warnings) = Parse(fileName, content, [new ArchiveRun(runId, DateTimeOffset.MaxValue, "")]);
        return (posting, warnings.FirstOrDefault());
    }

    public static (RawPosting? Posting, IReadOnlyList<string> Warnings) Parse(string fileName, string content, IReadOnlyList<ArchiveRun> runs)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var headers = Headers(lines, out var i);
        var body = string.Join('\n', lines.Skip(i)).Trim();

        if (!headers.TryGetValue("Title", out var title) || title.Length == 0 || body.Length == 0)
            return (null, [$"Inbox file {fileName} was skipped: it needs a 'Title:' line, a blank line, then the posting text."]);

        var warnings = new List<string>();
        var missing = new[] { "Company", "Location" }.Where(h => !headers.ContainsKey(h)).ToList();
        if (missing.Count > 0) warnings.Add($"Inbox file {fileName} has no {string.Join(" or ", missing)} line.");

        var ordered = runs.OrderBy(r => r.StartedAt).ToList();
        var runId = ordered.Count == 0 ? "" : ordered[^1].RunId;
        if (DateOnly.TryParseExact(headers.GetValueOrDefault("Saved", ""), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var saved))
            runId = ordered.FirstOrDefault(r => DateOnly.FromDateTime(r.StartedAt.Date) >= saved)?.RunId ?? runId;
        else
            warnings.Add($"Inbox file {fileName} has no 'Saved: yyyy-MM-dd' line, so it never ages out of the market data.");

        return (new RawPosting("inbox", fileName, runId, headers.GetValueOrDefault("Company", "Unknown"), title,
            headers.GetValueOrDefault("URL", ""), headers.GetValueOrDefault("Location", ""), Remote: null, Country: null,
            body, IsFullText: true, Salary: null), warnings);
    }
}
