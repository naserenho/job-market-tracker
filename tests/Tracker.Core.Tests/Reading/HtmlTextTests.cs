using Tracker.Reading;

namespace Tracker.Core.Tests.Reading;

public class HtmlTextTests
{
    [Fact]
    public void ToPlain_decodes_greenhouse_double_encoded_html_and_keeps_line_breaks()
    {
        var html = "&lt;p&gt;Build with &lt;strong&gt;C#&lt;/strong&gt; &amp;amp; .NET&lt;/p&gt;&lt;ul&gt;&lt;li&gt;Azure&lt;/li&gt;&lt;/ul&gt;";
        Assert.Equal("Build with C# & .NET\nAzure", HtmlText.ToPlain(html));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("plain  text", "plain text")]
    public void ToPlain_handles_empty_and_plain_input(string? input, string expected) =>
        Assert.Equal(expected, HtmlText.ToPlain(input));
}
