var builder = DistributedApplication.CreateBuilder(args);

// The Compose Postgres that the scheduled Processor rebuilds (port 5434). The connection string lives in this project's
// user-secrets as ConnectionStrings:tracker; Aspire doesn't start or own the database.
var tracker = builder.AddConnectionString("tracker");

// Liveness only (/alive): with the database down the API still runs and answers 503 or stale data, so the dashboard starts anyway.
var api = builder.AddProject<Projects.Api>("api")
    .WithReference(tracker)
    .WithHttpHealthCheck("/alive");

// Vite dev server on a fixed port so the page can be bookmarked; it proxies /api to the API address Aspire passes in.
builder.AddViteApp("dashboard", "../Dashboard")
    .WithReference(api)
    .WaitFor(api)
    .WithEndpoint("http", endpoint => endpoint.Port = 5190, createIfNotExists: false);

builder.Build().Run();
