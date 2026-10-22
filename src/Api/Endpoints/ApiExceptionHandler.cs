using Api.Applications;
using Api.Market;
using Api.Queries;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tracker.Data;

namespace Api.Endpoints;

// Known failures become problem details:
// - 400: bad filter or input;
// - 404 / 409: unknown or conflicting tracker entries;
// - 503: database down, or market tables from an older version (run the Processor);
// - 500: an unreadable backup, or anything else, without internals.
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log) : IExceptionHandler
{
    public const string StaleEdit = "This entry changed in another tab. Reload to see the latest.";

    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var result = exception switch
        {
            FilterException f => Results.Problem(statusCode: 400, title: "Invalid filter", detail: f.Message,
                extensions: new Dictionary<string, object?> { ["parameter"] = f.Parameter }),
            TrackerInputException t => Results.Problem(statusCode: 400, title: "Invalid input", detail: t.Message,
                extensions: new Dictionary<string, object?> { ["field"] = t.Field }),
            TrackerNotFoundException n => Results.Problem(statusCode: 404, title: "Not found", detail: n.Message),
            TrackerConflictException c => Results.Problem(statusCode: 409, title: "Conflict", detail: c.Message,
                extensions: new Dictionary<string, object?> { ["id"] = c.ExistingId }),
            DbUpdateConcurrencyException => Results.Problem(statusCode: 409, title: "Conflict", detail: StaleEdit),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                Results.Problem(statusCode: 409, title: "Conflict", detail: "That's already in your tracker. Reload to see it."),
            DatabaseUnavailableException d => Results.Problem(statusCode: 503, title: "Database unavailable", detail: d.Message),
            MarketSchemaOutdatedException o => Results.Problem(statusCode: 503, title: "Market data needs an update", detail: o.Message),
            _ when DatabaseErrors.IsUnavailable(exception) =>
                Results.Problem(statusCode: 503, title: "Database unavailable", detail: DatabaseUnavailableException.Text),
            TrackerBackupException b => Backup(b),
            _ => null,
        };
        if (result is null)
        {
            log.LogError(exception, "Unhandled error for {Path}", http.Request.Path);
            result = Results.Problem(statusCode: 500, title: "Something went wrong",
                detail: "The error was logged. See the API's structured logs in the Aspire dashboard.");
        }
        await result.ExecuteAsync(http);
        return true;
    }

    private IResult Backup(TrackerBackupException b)
    {
        log.LogError(b, "Tracker backup copy unreadable");
        return Results.Problem(statusCode: 500, title: "Tracker backup unreadable", detail: b.Message);
    }
}
