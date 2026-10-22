using System.Text.Json;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Reading;

public class SourceReadersTests
{
    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;
    private static readonly SourceContext Ctx = new("r1", RepoConfig.Toronto);

    [Fact]
    public void Adzuna_reads_snippet_location_and_real_salary()
    {
        var p = Assert.Single(SourceReaders.Adzuna(Root("""
            {"results":[{"id":"5869671117","title":"<strong>.Net</strong> Developer","company":{"display_name":"Astra"},
              "location":{"display_name":"Toronto, Ontario","area":["Canada","Ontario","Toronto"]},
              "description":"Design and maintain <strong>.NET</strong> apps…","redirect_url":"https://www.adzuna.ca/details/5869671117",
              "salary_min":90000,"salary_max":110000,"salary_is_predicted":"0"}]}
            """), Ctx));

        Assert.Equal("adzuna:5869671117", p.Key);
        Assert.Equal(".Net Developer", p.Title);
        Assert.Equal("Toronto, Ontario; Canada, Ontario, Toronto", p.Location);
        Assert.Equal("CA", p.Country);
        Assert.False(p.IsFullText);
        Assert.Equal(new SalaryRange(90000, 110000, SalaryPeriod.Year), p.Salary);
    }

    [Fact]
    public void Adzuna_ignores_predicted_salaries_and_skips_results_without_id()
    {
        var postings = SourceReaders.Adzuna(Root("""
            {"results":[{"id":"1","title":"Dev","salary_min":50000,"salary_is_predicted":"1"},{"title":"No id"}]}
            """), Ctx).ToList();
        Assert.Null(Assert.Single(postings).Salary);
    }

    [Fact]
    public void Greenhouse_decodes_content_and_uses_company_name()
    {
        var p = Assert.Single(SourceReaders.Greenhouse(Root("""
            {"jobs":[{"id":101,"title":"Senior Developer","company_name":"Acme Corp","location":{"name":"Toronto, Canada"},
              "absolute_url":"https://boards.greenhouse.io/acme/jobs/101","content":"&lt;p&gt;C# &amp;amp; Azure&lt;/p&gt;"}]}
            """), "acme", Ctx));

        Assert.Equal("greenhouse:101", p.Key);
        Assert.Equal("Acme Corp", p.Company);
        Assert.Equal("C# & Azure", p.Description);
        Assert.True(p.IsFullText);
    }

    [Fact]
    public void Lever_reads_locations_workplace_lists_and_cad_salary()
    {
        var p = Assert.Single(SourceReaders.Lever(Root("""
            [{"id":"L1","text":"Backend Developer","categories":{"location":"Remote or Mississauga","allLocations":["Remote or Mississauga","Toronto"]},
              "workplaceType":"remote","country":"CA","hostedUrl":"https://jobs.lever.co/wattpad/L1","descriptionPlain":"Java.",
              "lists":[{"text":"Requirements","content":"<li>Kafka</li>"}],"additionalPlain":"Benefits.",
              "salaryRange":{"min":158000,"max":198000,"currency":"CAD","interval":"per-year-salary"}}]
            """), "wattpad", Ctx));

        Assert.Equal("Wattpad", p.Company);
        Assert.Equal("Remote or Mississauga; Toronto", p.Location);
        Assert.True(p.Remote);
        Assert.Equal("CA", p.Country);
        Assert.Contains("Kafka", p.Description);
        Assert.Contains("Benefits.", p.Description);
        Assert.Equal(new SalaryRange(158000, 198000, SalaryPeriod.Year), p.Salary);
    }

    [Fact]
    public void Lever_ignores_usd_salary()
    {
        var p = Assert.Single(SourceReaders.Lever(Root("""
            [{"id":"L2","text":"Dev","salaryRange":{"min":150000,"max":180000,"currency":"USD","interval":"per-year-salary"}}]
            """), "x", Ctx));
        Assert.Null(p.Salary);
    }

    [Fact]
    public void Ashby_reads_remote_country_and_cad_compensation()
    {
        var p = Assert.Single(SourceReaders.Ashby(Root("""
            {"jobs":[{"id":"S1","title":"Engineer","location":"Toronto","secondaryLocations":[{"location":"Remote (Canada)"}],
              "isRemote":true,"workplaceType":"Hybrid","isListed":true,"jobUrl":"https://jobs.ashbyhq.com/cohere/S1",
              "address":{"postalAddress":{"addressCountry":"Canada","addressLocality":"Toronto"}},"descriptionPlain":"Python.",
              "compensation":{"compensationTiers":[{"components":[
                 {"compensationType":"EquityPercentage","currencyCode":null,"minValue":0.1,"maxValue":0.2},
                 {"compensationType":"Salary","interval":"1 YEAR","currencyCode":"CAD","minValue":77800,"maxValue":106920}]}]}},
             {"id":"S2","title":"Hidden","isListed":false}]}
            """), "cohere", Ctx));

        Assert.Equal("Toronto; Remote (Canada)", p.Location);
        Assert.False(p.Remote); // hybrid; remote-Canada still comes from the location text in ScopeRules
        Assert.Equal("CA", p.Country);
        Assert.Equal(new SalaryRange(77800, 106920, SalaryPeriod.Year), p.Salary);
    }

    // Field names as the public widget feed returns them (apply.workable.com, 2026-10-20).
    [Fact]
    public void Workable_reads_locations_remote_flag_country_and_description()
    {
        var postings = SourceReaders.Workable(Root("""
            {"name":"Fidus Systems","description":null,"jobs":[
              {"title":"Senior Hardware Designer","shortcode":"7B5FD1D1A1","code":"","employment_type":"Full-time","telecommuting":false,
               "department":"Engineering","url":"https://apply.workable.com/j/7B5FD1D1A1","shortlink":"https://apply.workable.com/j/7B5FD1D1A1",
               "application_url":"https://apply.workable.com/j/7B5FD1D1A1/apply","published_on":"2026-10-01","created_at":"2026-10-01",
               "country":"Canada","city":"Ottawa","state":"Ontario","education":"","experience":"","function":"","industry":"",
               "locations":[{"country":"Canada","countryCode":"CA","city":"Ottawa","region":"Ontario","hidden":false},
                            {"country":"Canada","countryCode":"CA","city":"Kitchener","region":"Ontario","hidden":false}],
               "description":"<p>FPGA &amp; board design.</p>"},
              {"title":"Field Engineer","shortcode":"C0FFEE","telecommuting":true,"url":"https://apply.workable.com/j/C0FFEE",
               "country":"United States","city":"Austin","state":"Texas",
               "locations":[{"country":"United States","countryCode":"US","city":"Austin","region":"Texas","hidden":false}],
               "description":"<p>Travel.</p>"},
              {"title":"No id","url":"https://apply.workable.com/j/x"}]}
            """), "fidus", new SourceContext("r1", RepoConfig.Ontario)).ToList();

        Assert.Equal(2, postings.Count);
        var ottawa = postings[0];
        Assert.Equal(("workable:7B5FD1D1A1", "Fidus Systems", "Senior Hardware Designer", "https://apply.workable.com/j/7B5FD1D1A1"),
            (ottawa.Key, ottawa.Company, ottawa.Title, ottawa.Url));
        Assert.Equal("Ottawa, Ontario, Canada; Kitchener, Ontario, Canada", ottawa.Location);
        Assert.Equal((false, "CA"), (ottawa.Remote, ottawa.Country));
        Assert.Equal("FPGA & board design.", ottawa.Description);
        Assert.True(ottawa.IsFullText);
        Assert.Null(ottawa.Salary);

        var austin = postings[1];
        Assert.Equal((true, "US", "Austin, Texas, United States"), (austin.Remote, austin.Country, austin.Location));
    }

    // List and detail field names as the public posting API returns them (api.smartrecruiters.com, 2026-10-20).
    private const string SrList = """
        {"offset":0,"limit":100,"totalFound":2,"content":[
          {"id":"744000153539213","name":"Technical Lead, Aerospace","company":{"identifier":"ADGAGroupConsultantsInc1","name":"ADGA Group Consultants Inc"},
           "location":{"city":"Ottawa","country":"ca","remote":false,"hybrid":true,"fullLocation":"Ottawa, , Canada"}},
          {"id":"744000153539214","name":"DevOps Developer","company":{"identifier":"ADGAGroupConsultantsInc1","name":"ADGA Group Consultants Inc"},
           "location":{"city":"Nepean","region":"ON","country":"ca","remote":true,"fullLocation":"Nepean, ON, Canada"}},
          {"name":"No id"}]}
        """;

    private const string SrDetail = """
        {"id":"744000153539213","postingUrl":"https://jobs.smartrecruiters.com/ADGAGroupConsultantsInc1/744000153539213-technical-lead",
         "jobAd":{"sections":{"companyDescription":{"title":"Company Description","text":""},
                              "jobDescription":{"title":"Job Description","text":"<p>Lead a C# team.</p>"},
                              "qualifications":{"title":"Qualifications","text":"<ul><li>Azure</li></ul>"},
                              "additionalInformation":{"title":"Additional Information","text":"<p>Secret clearance.</p>"}}},
         "compensation":{"min":140000,"max":160000,"currency":"CAD","period":"YEARLY"}}
        """;

    [Fact]
    public void Smartrecruiters_joins_the_list_with_each_posting_detail()
    {
        var postings = SourceReaders.SmartRecruiters(Root(SrList), "adgagroupconsultantsinc1",
            id => id == "744000153539213" ? Root(SrDetail) : null, new SourceContext("r1", RepoConfig.Ontario)).ToList();

        Assert.Equal(2, postings.Count);
        var lead = postings[0];
        Assert.Equal(("smartrecruiters:744000153539213", "ADGA Group Consultants Inc", "Technical Lead, Aerospace"), (lead.Key, lead.Company, lead.Title));
        Assert.Equal("https://jobs.smartrecruiters.com/ADGAGroupConsultantsInc1/744000153539213-technical-lead", lead.Url);
        Assert.Equal(("Ottawa, Canada", false, "CA"), (lead.Location, lead.Remote, lead.Country));
        Assert.Equal("Lead a C# team.\nAzure\nSecret clearance.", lead.Description);
        Assert.True(lead.IsFullText);
        Assert.Equal(new SalaryRange(140000, 160000, SalaryPeriod.Year), lead.Salary);

        // No detail saved: the posting still counts, without text.
        var devops = postings[1];
        Assert.Equal(("Nepean, ON, Canada", true, "", false), (devops.Location, devops.Remote, devops.Description, devops.IsFullText));
        Assert.Equal("https://jobs.smartrecruiters.com/adgagroupconsultantsinc1/744000153539214", devops.Url);
        Assert.Null(devops.Salary);
    }

    private static readonly SourceContext AbuDhabi = new("r1", RepoConfig.AbuDhabi);

    // UAE pay is usually monthly: a structured monthly range is a monthly salary, not a yearly one 12 times too low.
    [Fact]
    public void Lever_reads_a_monthly_salary_where_the_region_pays_monthly()
    {
        var p = Assert.Single(SourceReaders.Lever(Root("""
            [{"id":"L9","text":"Dev","salaryRange":{"min":25000,"max":35000,"currency":"AED","interval":"per-month-salary"}}]
            """), "x", AbuDhabi));

        Assert.Equal(new SalaryRange(25_000, 35_000, SalaryPeriod.Month), p.Salary);
    }

    [Fact]
    public void Ashby_reads_a_monthly_salary_where_the_region_pays_monthly()
    {
        var p = Assert.Single(SourceReaders.Ashby(Root("""
            {"jobs":[{"id":"S9","title":"Dev","location":"Abu Dhabi","compensation":{"compensationTiers":[{"components":[
               {"compensationType":"Salary","interval":"1 MONTH","currencyCode":"AED","minValue":25000,"maxValue":35000}]}]}}]}
            """), "x", AbuDhabi));

        Assert.Equal(new SalaryRange(25_000, 35_000, SalaryPeriod.Month), p.Salary);
    }

    // Toronto keeps reading every non-hourly interval as yearly, exactly as before.
    [Fact]
    public void Toronto_keeps_reading_non_hourly_intervals_as_yearly()
    {
        var p = Assert.Single(SourceReaders.Lever(Root("""
            [{"id":"L8","text":"Dev","salaryRange":{"min":9000,"max":10000,"currency":"CAD","interval":"per-month-salary"}}]
            """), "x", Ctx));

        Assert.Equal(SalaryPeriod.Year, p.Salary!.Period);
    }
}
