using Archiver.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Archiver;

public static class HttpSetup
{
    private const string UserAgent = "JobMarketTracker/0.1 (personal job-market research)";

    // The standard resilience handler retries 429/5xx with exponential backoff + jitter and honours Retry-After.
    public static IServiceCollection AddArchiverHttpClients(this IServiceCollection services)
    {
        // No retries: every wire call must count against the Adzuna budget; a failed page just ends that query.
        Register(services, AdzunaSource.HttpClientName, TimeSpan.FromSeconds(45),
            o => o.Retry.ShouldHandle = _ => ValueTask.FromResult(false));

        // Big boards (Stripe, Databricks) return several MB with full descriptions.
        Register(services, JobBoardSource.HttpClientName, TimeSpan.FromMinutes(5), o =>
        {
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
        });

        // Monthly CSVs are ~50 MB; the body is streamed after headers arrive and guarded by HttpArchiver's idle timeout.
        Register(services, JobBankSource.HttpClientName, TimeSpan.FromMinutes(10), o =>
        {
            o.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(8);
        });

        return services;
    }

    // The pipeline's timeouts end when response headers arrive, so HttpClient.Timeout (set just above the pipeline's
    // total) is what bounds reading a buffered body. It is configured after AddStandardResilienceHandler, which resets
    // HttpClient.Timeout to infinite.
    private static void Register(IServiceCollection services, string name, TimeSpan clientTimeout, Action<HttpStandardResilienceOptions> resilience)
    {
        var builder = services.AddHttpClient(name, c => c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent));
        builder.AddStandardResilienceHandler(resilience);
        builder.ConfigureHttpClient(c => c.Timeout = clientTimeout);
    }
}
