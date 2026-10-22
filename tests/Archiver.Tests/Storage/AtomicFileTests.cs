using Archiver.Storage;
using Archiver.Tests.TestSupport;

namespace Archiver.Tests.Storage;

public class AtomicFileTests
{
    [Fact]
    public async Task WriteAsync_creates_directories_and_leaves_no_tmp()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "a", "b", "x.json");

        await AtomicFile.WriteAsync(path, "hi"u8.ToArray(), CancellationToken.None);

        Assert.Equal("hi", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Failed_write_leaves_neither_file_nor_tmp()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "x.json");

        await Assert.ThrowsAsync<IOException>(() => AtomicFile.WriteAsync(path, async (stream, ct) =>
        {
            await stream.WriteAsync("partial"u8.ToArray(), ct);
            throw new IOException("boom");
        }, CancellationToken.None));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Cleanup_failure_does_not_hide_the_original_error()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "x.json");
        try
        {
            var ex = await Assert.ThrowsAsync<IOException>(() => AtomicFile.WriteAsync(path, (stream, ct) =>
            {
                File.SetAttributes(path + ".tmp", FileAttributes.ReadOnly); // makes deleting the tmp fail
                throw new IOException("boom");
            }, CancellationToken.None));

            Assert.Equal("boom", ex.Message);
        }
        finally
        {
            if (File.Exists(path + ".tmp")) File.SetAttributes(path + ".tmp", FileAttributes.Normal);
        }
    }
}
