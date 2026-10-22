using System.Text.Json;
using System.Text.Json.Serialization;
using Archiver.Storage;

namespace Archiver.Manifest;

public static class ManifestWriter
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Task WriteAsync(RunManifest manifest, string path, CancellationToken ct) =>
        AtomicFile.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(manifest, Json), ct);
}
