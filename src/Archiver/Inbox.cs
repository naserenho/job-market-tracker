namespace Archiver;

// data/inbox holds postings pasted by hand (LinkedIn, Indeed, bank career sites); phase 1 parses them.
public static class Inbox
{
    private const string ReadmeText = """
        # Job posting inbox

        Paste one job posting per `.txt` file here. Phase 1 of the tracker reads them.

        Suggested file name: `2026-10-01-company-job-title.txt`

        Start the file with these header lines, then a blank line, then the full posting text:

            URL: https://www.linkedin.com/jobs/view/...
            Company: Example Corp
            Title: Senior .NET Developer
            Location: Toronto, ON (Hybrid)
            Saved: 2026-10-01

        Only `.txt` files are counted. This folder is never committed to git.
        """;

    public static int EnsureAndCount(string dataRoot)
    {
        var dir = Path.Combine(dataRoot, "inbox");
        Directory.CreateDirectory(dir);
        var readme = Path.Combine(dir, "README.md");
        if (!File.Exists(readme)) File.WriteAllText(readme, ReadmeText);
        return Directory.EnumerateFiles(dir, "*.txt").Count();
    }
}
