namespace Archiver.Storage;

// Writes to "<path>.tmp" then renames, so a crash never leaves a half-written file that looks complete.
public static class AtomicFile
{
    public static Task WriteAsync(string path, byte[] content, CancellationToken ct) =>
        WriteAsync(path, (stream, token) => stream.WriteAsync(content, token).AsTask(), ct);

    public static async Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await write(stream, ct);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Best effort: a failed cleanup must not replace the error that caused it.
            try { File.Delete(tmp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
