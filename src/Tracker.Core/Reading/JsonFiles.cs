using System.IO.Compression;
using System.Text.Json;

namespace Tracker.Reading;

public static class JsonFiles
{
    public static bool IsJson(string path) =>
        path.EndsWith(".json", StringComparison.Ordinal) || path.EndsWith(".json.gz", StringComparison.Ordinal);

    // Raw runs may be compressed later to save disk; readers accept both forms.
    public static JsonDocument Load(string path)
    {
        using var file = File.OpenRead(path);
        using var stream = path.EndsWith(".gz", StringComparison.Ordinal)
            ? (Stream)new GZipStream(file, CompressionMode.Decompress)
            : file;
        return JsonDocument.Parse(stream);
    }

    public static string BaseName(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".json.gz", StringComparison.Ordinal)) return name[..^8];
        if (name.EndsWith(".json", StringComparison.Ordinal)) return name[..^5];
        return name;
    }
}
