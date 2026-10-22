using System.Net;
using Archiver.Sources;
using Archiver.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Archiver.Tests;

public class HttpSetupTests
{
    private static IHttpClientFactory Factory(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddArchiverHttpClients();
        extra?.Invoke(services);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    // The resilience pipeline's timeouts stop at the response headers; HttpClient.Timeout is what bounds a buffered body.
    [Theory]
    [InlineData(AdzunaSource.HttpClientName)]
    [InlineData(JobBoardSource.HttpClientName)]
    [InlineData(JobBankSource.HttpClientName)]
    public void Every_client_has_a_finite_overall_timeout(string name) =>
        Assert.NotEqual(Timeout.InfiniteTimeSpan, Factory().CreateClient(name).Timeout);

    [Fact]
    public async Task Adzuna_client_never_retries_so_every_wire_call_is_budgeted()
    {
        var stub = new StubHandler(_ => StubHandler.Json("{}", HttpStatusCode.ServiceUnavailable));
        var http = Factory(s => s.AddHttpClient(AdzunaSource.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => stub))
            .CreateClient(AdzunaSource.HttpClientName);

        using var response = await http.GetAsync("https://api.test/x");

        Assert.Single(stub.Requests);
    }
}
