using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Applications;

// Everything in the tracker, as one JSON document: the copy that brings the tracker back if the database is lost.
public sealed record MirrorFile(int Format, DateTimeOffset SavedAt, List<Company> Companies, List<JobApplication> Applications,
    List<ApplicationEvent> Events, List<FollowUp> FollowUps, List<Contact> Contacts, List<ApplicationDocument> Documents)
{
    public const int CurrentFormat = 1;
}

public static class MirrorStore
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Write a temp file beside the real one, then swap it in: a crash mid-write never leaves half a copy.
    public static void Write(string path, MirrorFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json));
        File.Move(temp, path, overwrite: true);
    }

    // Broken JSON, a locked file, or a file this process may not open: all leave the copy unusable but intact.
    public static bool IsUnreadable(Exception ex) =>
        ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException;

    public static MirrorFile? Read(string path)
    {
        if (!File.Exists(path)) return null;
        MirrorFile? file;
        try
        {
            file = JsonSerializer.Deserialize<MirrorFile>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            throw new TrackerBackupException(
                $"{path} can't be read ({ex.Message}). Fix or move the file; the tracker stays closed until then so the copy isn't overwritten.", ex);
        }
        if (file is null || file.Format != MirrorFile.CurrentFormat)
            throw new TrackerBackupException(
                $"{path} has format {file?.Format.ToString() ?? "none"}; this version reads format {MirrorFile.CurrentFormat}.",
                new InvalidDataException("Unexpected mirror format."));
        return file;
    }
}
