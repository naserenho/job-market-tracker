namespace Tracker.Core.Tests;

public class ErrorTextTests
{
    [Fact]
    public void Chain_joins_inner_messages() =>
        Assert.Equal("outer -> inner", ErrorText.Chain(new InvalidOperationException("outer", new IOException("inner"))));
}
