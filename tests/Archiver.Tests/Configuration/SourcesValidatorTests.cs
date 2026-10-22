using Archiver.Configuration;

namespace Archiver.Tests.Configuration;

public class SourcesValidatorTests
{
    private static SourcesOptions Valid() => new()
    {
        Region = "toronto",
        Adzuna = new AdzunaOptions { Country = "ca", Where = "Toronto", DistanceKm = 50, Terms = ["tech lead"] },
        Boards = [new BoardConfig { Ats = "greenhouse", Slug = "faire" }],
        JobBank = new JobBankOptions { DatasetId = "abc" },
    };

    [Fact]
    public void Valid_options_have_no_errors() => Assert.Empty(SourcesValidator.Validate(Valid()));

    [Fact]
    public void Empty_terms_are_reported()
    {
        var o = Valid();
        o.Adzuna.Terms.Clear();
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("Adzuna:Terms"));
    }

    [Fact]
    public void Terms_that_collide_as_file_names_are_reported()
    {
        var o = Valid();
        o.Adzuna.Terms.Add("Tech Lead!");
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("tech-lead"));
    }

    [Fact]
    public void Unknown_ats_is_reported()
    {
        var o = Valid();
        o.Boards.Add(new BoardConfig { Ats = "workday", Slug = "rbc" });
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("workday"));
    }

    // Recruitee needs a token (not a public feed), so it stays unknown.
    [Fact]
    public void Workable_is_a_known_board_type_and_recruitee_is_not()
    {
        var o = Valid();
        o.Boards.Add(new BoardConfig { Ats = "workable", Slug = "fidus" });
        o.Boards.Add(new BoardConfig { Ats = "smartrecruiters", Slug = "GDMSI" });
        Assert.Empty(SourcesValidator.Validate(o));

        o.Boards.Add(new BoardConfig { Ats = "recruitee", Slug = "acme" });
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("recruitee"));
    }

    [Fact]
    public void Duplicate_boards_are_reported()
    {
        var o = Valid();
        o.Boards.Add(new BoardConfig { Ats = "greenhouse", Slug = "Faire" });
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("greenhouse-faire.json"));
    }

    [Fact]
    public void Boards_whose_slugs_collide_as_file_names_are_reported()
    {
        var o = Valid();
        o.Boards.Add(new BoardConfig { Ats = "greenhouse", Slug = "foo-bar" });
        o.Boards.Add(new BoardConfig { Ats = "greenhouse", Slug = "foo.bar" });
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("greenhouse-foo-bar.json"));
    }

    [Fact]
    public void Empty_board_list_is_reported()
    {
        var o = Valid();
        o.Boards.Clear();
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("Boards"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Results_per_page_must_be_1_to_50(int value)
    {
        var o = Valid();
        o.Adzuna.ResultsPerPage = value;
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("ResultsPerPage"));
    }

    [Fact]
    public void Delay_below_adzunas_25_per_minute_limit_is_reported()
    {
        var o = Valid();
        o.Adzuna.MinDelayMs = 300;
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("MinDelayMs"));
    }

    [Fact]
    public void Budget_above_adzunas_daily_limit_is_reported()
    {
        var o = Valid();
        o.Adzuna.MaxCallsPerRun = 300;
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("MaxCallsPerRun"));
    }

    [Fact]
    public void A_job_bank_backfill_below_one_is_reported()
    {
        var o = Valid();
        o.JobBank.BackfillMonths = 0;
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("BackfillMonths"));
    }

    [Fact]
    public void A_missing_region_is_reported()
    {
        var o = Valid();
        o.Region = "";
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("Region"));
    }

    // A region without Adzuna (Abu Dhabi) or without Job Bank is valid: those sources are simply skipped.
    [Fact]
    public void Adzuna_and_job_bank_rules_apply_only_when_the_region_uses_them()
    {
        var o = Valid();
        o.Adzuna.Country = "";
        o.Adzuna.DistanceKm = 0;
        o.JobBank.DatasetId = "";

        Assert.Empty(SourcesValidator.Validate(o));
    }

    [Fact]
    public void Adzuna_distance_may_be_left_out_but_not_negative()
    {
        var o = Valid();
        o.Adzuna.DistanceKm = 0;
        Assert.Empty(SourcesValidator.Validate(o));

        o.Adzuna.DistanceKm = -1;
        Assert.Contains("Adzuna:DistanceKm must be at least 1 when set.", SourcesValidator.Validate(o));
    }

    [Fact]
    public void An_empty_board_list_names_the_region_file()
    {
        var o = Valid();
        o.Boards.Clear();
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("config/regions/toronto.json"));
    }
}
