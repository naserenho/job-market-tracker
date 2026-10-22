using System.IO.Compression;
using System.Text;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Reading;

public class JsonFilesTests
{
    [Fact]
    public void Load_reads_plain_and_gzipped_json()
    {
        using var tmp = new TempDir();
        var plain = tmp.File("a.json", "{\"n\":1}");
        var gz = Path.Combine(tmp.Path, "b.json.gz");
        using (var file = File.Create(gz))
        using (var zip = new GZipStream(file, CompressionLevel.Fastest))
            zip.Write(Encoding.UTF8.GetBytes("{\"n\":2}"));

        using var a = JsonFiles.Load(plain);
        using var b = JsonFiles.Load(gz);

        Assert.Equal(1, a.RootElement.GetProperty("n").GetInt32());
        Assert.Equal(2, b.RootElement.GetProperty("n").GetInt32());
    }

    [Theory]
    [InlineData("x/greenhouse-faire.json", "greenhouse-faire", true)]
    [InlineData("x/ashby-cohere.json.gz", "ashby-cohere", true)]
    [InlineData("x/notes.txt", "notes.txt", false)]
    [InlineData("x/a.json.tmp", "a.json.tmp", false)]
    public void Names_and_extensions(string path, string baseName, bool isJson)
    {
        Assert.Equal(baseName, JsonFiles.BaseName(path));
        Assert.Equal(isJson, JsonFiles.IsJson(path));
    }
}
