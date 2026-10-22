using System.Text;
using Tracker.Analysis;
using Tracker.Configuration;

namespace Tracker.Reporting;

// Writes data/reports/<run>.html, latest.fragment.html (for publishing) and latest.html last, each via temp file + rename.
public static class ReportWriter
{
    public static async Task<string> WriteAsync(string dataRoot, ReportModel model, Region region, CancellationToken ct)
    {
        var fragment = ReportRenderer.RenderFragment(model, region);
        var document = ReportRenderer.RenderDocument(fragment);
        var dir = Path.Combine(dataRoot, "reports");
        Directory.CreateDirectory(dir);

        await WriteAtomicAsync(Path.Combine(dir, $"{model.LatestRunId}.html"), document, ct);
        await WriteAtomicAsync(Path.Combine(dir, "latest.fragment.html"), fragment, ct);
        var latest = Path.Combine(dir, "latest.html");
        await WriteAtomicAsync(latest, document, ct);
        return latest;
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(tmp, path, overwrite: true);
    }
}
