using Npgsql;

namespace Api.Applications;

// Runs in front of every tracker endpoint.
public sealed class TrackerGate(TrackerSchema schema) : IEndpointFilter
{
    public const string ClientHeader = "X-Tracker-Client";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        // Changes need a custom header. A browser can't add one to another website's form post without a CORS preflight,
        // which this API never answers, so no other site can change the tracker through the owner's browser.
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !request.Headers.ContainsKey(ClientHeader))
            return Results.Problem(statusCode: 403, title: "Forbidden", detail: "Tracker changes must come from the dashboard.");
        await schema.EnsureReadyAsync(context.HttpContext.RequestAborted);
        try
        {
            return await next(context);
        }
        catch (Exception ex) when (TablesMissing(ex))
        {
            // The tables were there when this API first used them and are gone now (a lost Docker volume). The next
            // request recreates them and restores from the backup copy.
            schema.Reset();
            return Results.Problem(statusCode: 503, title: "Tracker tables missing",
                detail: "The tracker's tables were missing, so they are being recreated from the backup copy. Please try again.");
        }
    }

    private static bool TablesMissing(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable }) return true;
        return false;
    }
}
