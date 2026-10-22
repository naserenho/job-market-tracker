using Api.Applications;

namespace Api.Tests.Applications;

public class RulesTests
{
    [Fact]
    public void Required_text_is_trimmed_and_names_the_field_when_missing()
    {
        Assert.Equal("Senior Dev", Rules.Required("  Senior Dev ", "title"));
        var error = Assert.Throws<TrackerInputException>(() => Rules.Required("   ", "postingText"));
        Assert.Equal(("postingText", "Job text is required."), (error.Field, error.Message));
    }

    [Fact]
    public void Text_over_the_limit_is_refused()
    {
        var error = Assert.Throws<TrackerInputException>(() => Rules.Optional(new string('x', 201), "teamSize"));
        Assert.Equal("teamSize", error.Field);
        Assert.Contains("Team size is longer than 200", error.Message);
    }

    [Fact]
    public void Single_line_collapses_breaks_and_company_keys_ignore_case_and_spacing()
    {
        Assert.Equal("Senior .NET Developer (Payments)", Rules.SingleLine("Senior .NET Developer\r\n (Payments) "));
        Assert.Equal(Rules.CompanyKey("RBC"), Rules.CompanyKey("  rbc "));
        Assert.Equal("rbc capital markets", Rules.CompanyKey("RBC \n Capital   Markets"));
    }

    [Theory]
    [InlineData("https://careers.example.com/1", true)]
    [InlineData("http://example.com", true)]
    [InlineData("", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("www.example.com/job", false)]
    [InlineData("file:///C:/secret.txt", false)]
    public void Links_must_be_web_addresses(string value, bool ok)
    {
        if (ok) Assert.Equal(value, Rules.OptionalUrl(value, "url"));
        else Assert.Equal("url", Assert.Throws<TrackerInputException>(() => Rules.OptionalUrl(value, "url")).Field);
    }

    [Fact]
    public void Emails_need_a_plausible_shape()
    {
        Assert.Equal("", Rules.OptionalEmail(" ", "email"));
        Assert.Equal("sam@rbc.com", Rules.OptionalEmail("sam@rbc.com", "email"));
        Assert.Equal("email", Assert.Throws<TrackerInputException>(() => Rules.OptionalEmail("sam at rbc", "email")).Field);
    }

    [Fact]
    public void Enums_parse_by_name_ignoring_case_and_refuse_numbers()
    {
        Assert.Equal(ApplicationStatus.Interviewing, Rules.ParseEnum<ApplicationStatus>("interviewing", "status"));
        Assert.Equal("status", Assert.Throws<TrackerInputException>(() => Rules.ParseEnum<ApplicationStatus>("3", "status")).Field);
        var error = Assert.Throws<TrackerInputException>(() => Rules.ParseEnum<ApplicationStatus>("wizard", "status"));
        Assert.Contains("Shortlisted", error.Message);
    }

    [Theory]
    [InlineData("CV.pdf", "CV.pdf")]
    [InlineData("../../evil name?.pdf", "evil-name-.pdf")]
    [InlineData("..\\CV (RBC).docx", "CV-RBC-.docx")]
    [InlineData("C:\\Users\\me\\Cover letter.txt", "Cover-letter.txt")]
    [InlineData("???", "file")]
    public void File_names_lose_folders_and_odd_characters(string name, string expected) =>
        Assert.Equal(expected, Rules.SafeFileName(name));

    [Fact]
    public void Uploads_are_limited_by_type_and_size()
    {
        Rules.CheckUpload("CV.PDF", 1000);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", Rules.ContentType("x.DOCX"));
        Assert.Contains("PDF, DOCX", Assert.Throws<TrackerInputException>(() => Rules.CheckUpload("tool.exe", 10)).Message);
        Assert.Contains("empty", Assert.Throws<TrackerInputException>(() => Rules.CheckUpload("CV.pdf", 0)).Message);
        Assert.Contains("10 MB", Assert.Throws<TrackerInputException>(() => Rules.CheckUpload("CV.pdf", Rules.MaxFileBytes + 1)).Message);
    }

    [Theory]
    [InlineData("postingText", "Job text")]
    [InlineData("url", "Link")]
    [InlineData("linkedIn", "LinkedIn")]
    [InlineData("salaryAsked", "Salary asked")]
    public void Labels_read_like_the_form(string field, string label) => Assert.Equal(label, Rules.Label(field));

    // Enum.TryParse reads a comma list as a bitwise OR: "Shortlisted,Applied" would silently become one status.
    [Fact]
    public void A_comma_joined_enum_value_is_refused() =>
        Assert.Equal("status", Assert.Throws<TrackerInputException>(() => Rules.ParseEnum<ApplicationStatus>("Shortlisted,Applied", "status")).Field);

    [Fact]
    public void A_nul_character_is_refused_in_any_text_field()
    {
        Assert.Equal("title", Assert.Throws<TrackerInputException>(() => Rules.Required("Dev\0", "title")).Field);
        Assert.Contains("NUL", Assert.Throws<TrackerInputException>(() => Rules.Optional("a\0b", "notes", Rules.MaxLongText)).Message);
    }
}
