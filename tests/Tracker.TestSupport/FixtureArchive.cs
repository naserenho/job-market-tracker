using System.Text.Json;

namespace Tracker.TestSupport;

// A small, realistic archive:
//  run 1 (2026-09-01): adzuna A1 (.NET Developer, Toronto, salary) + A2 (Data Engineer, Toronto);
//                      greenhouse-acme G1 (Senior Full Stack Developer, Toronto, CAD salary in text) + G2 (Bellevue, US)
//  run 2 (2026-09-15): A1 again, G1 again, lever-wattpad L1 (Backend Developer, Toronto hybrid, structured CAD salary)
//  run 3 (2026-10-01): G1 again, ashby-cohere S1 (Senior Software Engineer, Remote (Canada), structured CAD salary),
//                      adzuna A3 = the same job as G1 (cross-source duplicate), a histogram aggregate
//  plus: raw/_dryrun (ignored), raw/broken-run without manifest (ignored), inbox/one.txt (LinkedIn paste)
public static class FixtureArchive
{
    public const string Run1 = "2026-09-01_0700";
    public const string Run2 = "2026-09-15_0700";
    public const string Run3 = "2026-10-01_0700";

    public static void Write(string dataRoot)
    {
        Manifest(dataRoot, Run1, "2026-09-01T07:00:00-04:00");
        Manifest(dataRoot, Run2, "2026-09-15T07:00:00-04:00");
        Manifest(dataRoot, Run3, "2026-10-01T07:00:00-04:00");
        Manifest(dataRoot, "_dryrun", "2026-10-02T07:00:00-04:00");
        Put(dataRoot, "raw/_dryrun/adzuna/search/x-gta-p1.json", Adzuna(("DRY", ".NET Developer", "Acme", "Toronto, Ontario", "C#", null)));
        Put(dataRoot, "raw/broken-run/adzuna/search/x-gta-p1.json", Adzuna(("BROKEN", ".NET Developer", "Acme", "Toronto, Ontario", "C#", null)));

        Put(dataRoot, $"raw/{Run1}/adzuna/search/net-developer-gta-p1.json", Adzuna(
            ("A1", ".NET Developer", "Northwind", "Toronto, Ontario", "Build services in C# and .NET on Azure.", 100000),
            ("A2", "Senior Data Engineer", "Contoso", "Toronto, Ontario", "Spark and Python pipelines.", null)));
        Put(dataRoot, $"raw/{Run1}/boards/greenhouse-acme.json", Greenhouse(G1, G2));

        Put(dataRoot, $"raw/{Run2}/adzuna/search/net-developer-gta-p1.json", Adzuna(
            ("A1", ".NET Developer", "Northwind", "Toronto, Ontario", "Build services in C# and .NET on Azure.", 100000)));
        Put(dataRoot, $"raw/{Run2}/boards/greenhouse-acme.json", Greenhouse(G1));
        Put(dataRoot, $"raw/{Run2}/boards/lever-wattpad.json", """
            [{"id":"L1","text":"Backend Developer","categories":{"location":"Toronto","allLocations":["Toronto"]},
              "workplaceType":"hybrid","country":"CA","hostedUrl":"https://jobs.lever.co/wattpad/L1",
              "descriptionPlain":"Java and Kafka services.","lists":[{"text":"Nice to have","content":"<li>Kubernetes</li>"}],
              "additionalPlain":"","salaryRange":{"min":110000,"max":140000,"currency":"CAD","interval":"per-year-salary"}}]
            """);

        Put(dataRoot, $"raw/{Run3}/boards/greenhouse-acme.json", Greenhouse(G1));
        Put(dataRoot, $"raw/{Run3}/boards/ashby-cohere.json", """
            {"jobs":[{"id":"S1","title":"Senior Software Engineer","location":"Remote (Canada)","secondaryLocations":[],
              "isRemote":true,"workplaceType":"Remote","isListed":true,"jobUrl":"https://jobs.ashbyhq.com/cohere/S1",
              "address":{"postalAddress":{"addressCountry":"Canada"}},
              "descriptionPlain":"Python, Kubernetes and AKS. Canadian citizenship required.",
              "compensation":{"compensationTiers":[{"components":[{"compensationType":"Salary","interval":"1 YEAR","currencyCode":"CAD","minValue":150000,"maxValue":200000}]}]}}]}
            """);
        Put(dataRoot, $"raw/{Run3}/adzuna/search/full-stack-developer-gta-p1.json", Adzuna(
            ("A3", "Senior Full Stack Developer", "Acme", "Toronto, Ontario", "React and C#...", null)));
        Put(dataRoot, $"raw/{Run3}/adzuna/histogram/net-developer-gta.json", """{"histogram":{"100000":7}}""");

        Put(dataRoot, "inbox/one.txt", "URL: https://www.linkedin.com/jobs/view/1\nCompany: Bank of Example\nTitle: Senior .NET Developer\nLocation: Toronto, ON\nSaved: 2026-09-30\n\nWe need C#, SQL Server and Azure DevOps.\n");
    }

    private const string G1 = """
        {"id":101,"title":"Senior Full Stack Developer","company_name":"Acme","location":{"name":"Toronto, Ontario, Canada"},
         "absolute_url":"https://boards.greenhouse.io/acme/jobs/101",
         "content":"&lt;p&gt;React, TypeScript and C#. The salary range in Toronto is $130,000 - $150,000.&lt;/p&gt;"}
        """;

    private const string G2 = """
        {"id":102,"title":"Software Engineer","company_name":"Acme","location":{"name":"Bellevue, Washington"},
         "absolute_url":"https://boards.greenhouse.io/acme/jobs/102","content":"&lt;p&gt;Go and Rust.&lt;/p&gt;"}
        """;

    private static string Greenhouse(params string[] jobs) => $$"""{"jobs":[{{string.Join(",", jobs)}}]}""";

    private static string Adzuna(params (string Id, string Title, string Company, string Location, string Description, int? Salary)[] results) =>
        JsonSerializer.Serialize(new
        {
            count = results.Length,
            results = results.Select(r =>
            {
                var job = new Dictionary<string, object?>
                {
                    ["id"] = r.Id,
                    ["title"] = r.Title,
                    ["company"] = new { display_name = r.Company },
                    ["location"] = new { display_name = r.Location, area = new[] { "Canada", "Ontario", "Toronto" } },
                    ["description"] = r.Description,
                    ["redirect_url"] = $"https://www.adzuna.ca/details/{r.Id}",
                };
                if (r.Salary is { } s)
                {
                    job["salary_min"] = s;
                    job["salary_max"] = s + 20000;
                    job["salary_is_predicted"] = "0";
                }
                return job;
            }),
        });

    private static void Manifest(string dataRoot, string runId, string startedAt) =>
        Put(dataRoot, $"raw/{runId}/manifest.json", $$"""{"runId":"{{runId}}","startedAt":"{{startedAt}}"}""");

    private static void Put(string dataRoot, string relative, string content)
    {
        var full = Path.Combine(dataRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
