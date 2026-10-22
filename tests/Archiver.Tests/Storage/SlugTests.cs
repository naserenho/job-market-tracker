using Archiver.Storage;

namespace Archiver.Tests.Storage;

public class SlugTests
{
    [Theory]
    [InlineData(".net developer", "net-developer")]
    [InlineData("c# developer", "csharp-developer")]
    [InlineData("Senior  Software Engineer", "senior-software-engineer")]
    [InlineData("C++", "cplusplus")]
    [InlineData("  tech lead!! ", "tech-lead")]
    [InlineData("!!!", "")]
    public void From_makes_file_safe_slugs(string input, string expected) =>
        Assert.Equal(expected, Slug.From(input));
}
