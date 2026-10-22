using System.Text.Json;

namespace Archiver.Tests;

public class ErrorTextTests
{
    [Fact]
    public void Chain_keeps_the_inner_detail_that_locates_a_config_error()
    {
        var ex = new InvalidDataException("Failed to load configuration from file 'sources.json'.",
            new JsonException("'}' is invalid after a value. LineNumber: 4 | BytePositionInLine: 2."));

        var text = ErrorText.Chain(ex);

        Assert.Contains("Failed to load configuration", text);
        Assert.Contains("LineNumber: 4", text);
    }

    [Fact]
    public void Chain_of_a_single_exception_is_its_message() =>
        Assert.Equal("boom", ErrorText.Chain(new InvalidOperationException("boom")));
}
