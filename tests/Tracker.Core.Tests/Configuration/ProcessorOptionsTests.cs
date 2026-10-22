using Tracker.TestSupport;

namespace Tracker.Core.Tests.Configuration;

public class ProcessorOptionsTests
{
    [Fact]
    public void Committed_processor_json_loads() => Assert.Equal(30, RepoConfig.Options().WindowDays);

    [Theory]
    [InlineData("1password", "1Password")]
    [InlineData("stripe", "Stripe")]
    public void CompanyName_uses_the_regions_map_then_capitalizes_the_slug(string slug, string expected) =>
        Assert.Equal(expected, RepoConfig.Toronto.CompanyName(slug));
}
