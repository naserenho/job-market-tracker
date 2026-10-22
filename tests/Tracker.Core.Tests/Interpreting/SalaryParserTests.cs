using Tracker.Interpreting;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Interpreting;

public class SalaryParserTests
{
    private static readonly SalaryParser Toronto = new(RepoConfig.Toronto.Salary), AbuDhabi = new(RepoConfig.AbuDhabi.Salary);

    private static void AssertRange(SalaryRange? actual, decimal min, decimal max, SalaryPeriod period = SalaryPeriod.Year)
    {
        Assert.NotNull(actual);
        Assert.Equal(new SalaryRange(min, max, period), actual);
    }

    [Fact]
    public void Plain_dollar_range_in_a_canadian_posting() =>
        AssertRange(Toronto.FromText("The base salary range for this role in Toronto is $120,000 - $150,000.", true), 120_000, 150_000);

    [Fact]
    public void Canadian_range_wins_over_the_us_range() =>
        AssertRange(Toronto.FromText("US: $150,000–$190,000 USD\nCanada: CA$130,000–CA$165,000", true), 130_000, 165_000);

    [Fact]
    public void K_suffix_on_both_sides() =>
        AssertRange(Toronto.FromText("Salary range: $180K - $220K", true), 180_000, 220_000);

    [Fact]
    public void K_suffix_only_on_the_upper_bound() =>
        AssertRange(Toronto.FromText("Pay: $120-150K", true), 120_000, 150_000);

    [Fact]
    public void Ashby_style_ca_dollar_k() =>
        AssertRange(Toronto.FromText("CA$215K – CA$310K • Offers Equity", true), 215_000, 310_000);

    [Fact]
    public void Hourly_rate() =>
        AssertRange(Toronto.FromText("Rate: $55 - $65 per hour", true), 55, 65, SalaryPeriod.Hour);

    [Fact]
    public void Stipends_are_skipped_in_favour_of_the_salary() =>
        AssertRange(Toronto.FromText("$500 - $1,000 learning stipend. Salary $100,000 to $120,000 per year.", true), 100_000, 120_000);

    [Fact]
    public void Ontario_pay_transparency_wording() =>
        AssertRange(Toronto.FromText("Ontario Pay Transparency Range CA$77,800 – CA$106,920", true), 77_800, 106_920);

    [Fact]
    public void Between_cad_and_cad() =>
        AssertRange(Toronto.FromText("The salary range for this role is between $93,000 CAD and $110,000 CAD.", true), 93_000, 110_000);

    [Fact]
    public void Us_label_after_the_range_is_skipped_for_the_canadian_one() =>
        AssertRange(Toronto.FromText("The range is $170,000 - $200,000 in the US and $130,000 - $160,000 in Canada.", true), 130_000, 160_000);

    [Fact]
    public void Monthly_stipend_before_the_salary_is_skipped() =>
        AssertRange(Toronto.FromText("$50–$100 monthly phone stipend. Salary $100,000 to $120,000.", true), 100_000, 120_000);

    [Fact]
    public void Bonus_before_the_base_salary_is_skipped() =>
        AssertRange(Toronto.FromText("Annual bonus of $30,000 - $50,000. Base salary $140,000 - $170,000.", true), 140_000, 170_000);

    [Fact]
    public void Base_salary_followed_by_plus_bonus_is_kept() =>
        AssertRange(Toronto.FromText("Base salary $140,000 - $170,000 plus bonus and equity.", true), 140_000, 170_000);

    [Fact]
    public void Hourly_rate_without_a_unit_marker() =>
        AssertRange(Toronto.FromText("Pay Rate: $42.31 - $58.17", true), 42.31m, 58.17m, SalaryPeriod.Hour);

    [Theory]
    [InlineData("For candidates located in NYC or SF, the pay range for this role is $167,000 - $220,000", true)]
    [InlineData("Internet allowance of $100 - $150 per month.", true)]
    [InlineData("US base pay range $150,000-$200,000 for New York", true)]
    [InlineData("Compensation: $140,000 - $160,000", false)]
    [InlineData("Salary: $150,000 - $200,000 USD", true)]
    [InlineData("We offer competitive pay and $2,000 - $3,000 in perks.", true)]
    [InlineData("No numbers here.", true)]
    public void No_cad_salary(string text, bool inCanada) =>
        Assert.Null(Toronto.FromText(text, inCanada));

    // The Toronto file must rebuild exactly the patterns this class used to hard-code.
    [Fact]
    public void Toronto_patterns_equal_the_old_hand_written_ones()
    {
        Assert.Equal(@"(?<c1>CA\$|C\$|CAD\s?\$?|US\$|USD\s?\$?|\$)\s?(?<a>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s?(?<ka>[kK])?\s?(?<t1>CAD|USD)?\s*(?:-|–|—|to|and)\s*(?<c2>CA\$|C\$|CAD\s?\$?|US\$|USD\s?\$?|\$)?\s?(?<b>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s?(?<kb>[kK])?(?<t2>\s?(?:CAD|USD|CA|US)\b)?(?<per>\s*(?:/|per|an|a)\s*(?:hour|hr|year|yr|annum)\b|\s*(?:hourly|annually|per annum)\b)?",
            Toronto.RangePatternText);
        Assert.Equal(@"\b(?:canada|canadian|toronto|ontario|vancouver|montreal|cad)\b", Toronto.ContextPatternText);
        Assert.Equal(@"\b(?:united states|u\.s\.|usa|us-based|us base|new york|nyc|san francisco|bay area|california|seattle|washington|colorado|texas|massachusetts|boston|chicago|austin)\b|(?-i:\b(?:US|SF)\b)",
            Toronto.ForeignContextPatternText);
        Assert.Equal(@"^\s*(?:CAD|USD)?\s*(?:(?:per|a|an|/)\s*(?:month|mo|week|wk)\b|(?:monthly|weekly|biweekly)\b|(?:(?!plus\b|and\b|with\b)[\w-]+\s+){0,2}(?:stipend|allowance|bonus|budget|reimbursement|credit)\b)",
            Toronto.NonSalaryAfterPatternText);
    }

    [Theory]
    [InlineData("Salary: AED 25,000 - 35,000 per month", 25000, 35000)]
    [InlineData("AED25,000-AED35,000/month", 25000, 35000)]
    [InlineData("AED 25k - 35k monthly", 25000, 35000)]
    [InlineData("Salary: 30,000 - 40,000 AED per month", 30000, 40000)]
    [InlineData("Dhs 20,000 to 28,000 a month", 20000, 28000)]
    public void Abu_Dhabi_reads_monthly_AED_salaries(string text, int min, int max) =>
        AssertRange(AbuDhabi.FromText(text, postingInCountry: true), min, max, SalaryPeriod.Month);

    [Fact]
    public void Abu_Dhabi_reads_a_yearly_package() =>
        AssertRange(AbuDhabi.FromText("Package AED 400,000 - 500,000 per annum", true), 400_000, 500_000);

    [Fact]
    public void Abu_Dhabi_monthly_salaries_count_twelve_times_a_year()
    {
        var range = AbuDhabi.FromText("AED 25,000 - 35,000 per month", true)!;
        Assert.Equal((SalaryPeriod.Month, 300_000m, 420_000m), (range.Period, range.YearlyMin, range.YearlyMax));
    }

    [Theory]
    [InlineData("USD 120,000 - 150,000 per year")]
    [InlineData("$120,000 - $150,000")]                       // a bare dollar is foreign in the UAE
    [InlineData("SAR 20,000 - 30,000 per month")]
    [InlineData("Housing allowance of AED 5,000 - 8,000 per month")]
    [InlineData("Ages 25 - 40")]                              // no currency at all
    public void Abu_Dhabi_refuses_foreign_and_non_salary_amounts(string text) =>
        Assert.Null(AbuDhabi.FromText(text, true));

    [Fact]
    public void Toronto_still_treats_a_monthly_range_as_not_a_salary() =>
        Assert.Null(Toronto.FromText("$5,000 - $6,000 per month", true));
}
