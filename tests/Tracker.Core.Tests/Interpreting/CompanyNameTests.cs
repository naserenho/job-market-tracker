using Tracker.Interpreting;

namespace Tracker.Core.Tests.Interpreting;

public class CompanyNameTests
{
    [Theory]
    [InlineData("Acme", "Acme Inc.")]
    [InlineData("  acme  ", "ACME, Ltd")]
    [InlineData("The Globex Corporation", "Globex")]
    public void Variants_share_a_key(string a, string b) => Assert.Equal(CompanyName.Key(a), CompanyName.Key(b));

    [Fact]
    public void Different_companies_differ() => Assert.NotEqual(CompanyName.Key("Acme"), CompanyName.Key("Acme Labs"));

    // A name made only of dropped words keeps a key of its own, so such companies don't all merge.
    [Fact]
    public void A_name_of_only_noise_words_keeps_its_own_key()
    {
        Assert.Equal("the company inc.", CompanyName.Key(" The Company Inc. "));
        Assert.NotEqual(CompanyName.Key("The Company Inc."), CompanyName.Key("Co Ltd"));
    }

    [Fact]
    public void The_most_frequent_spelling_is_shown_then_ordinal_order()
    {
        Assert.Equal("Acme Inc.", CompanyName.Display(["Acme", "Acme Inc.", " Acme Inc. "]));
        Assert.Equal("Acme", CompanyName.Display(["Acme Inc.", "Acme"]));
    }
}
