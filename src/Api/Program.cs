using System.Text.Json.Serialization;
using Api;
using Api.Applications;
using Api.Cv;
using Api.Endpoints;
using Api.Market;
using Microsoft.EntityFrameworkCore;
using Tracker.Configuration;
using Tracker.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var root = RepoRoot.Find(builder.Environment.ContentRootPath) ?? RepoRoot.Find(AppContext.BaseDirectory)
    ?? throw new InvalidOperationException("config/sources.json was not found in the API folder or any parent.");
builder.Services.AddSingleton(MarketSettings.Load(root, builder.Configuration["Tracker:ProfilePath"], builder.Configuration["Tracker:DataRoot"], builder.Configuration["Tracker:Region"]));
builder.Services.AddSingleton(new CvSettings(builder.Configuration["Tracker:CvPath"] ?? Path.Combine(root, "config", "cv.json")));

// Read-only use of the database the Processor rebuilds. No retries: a stopped database should answer 503 at once.
builder.AddNpgsqlDbContext<TrackerDb>("tracker",
    settings => settings.DisableRetry = true,
    options => options.UseSnakeCaseNamingConvention());

// The owner's tracker tables: same database, own context and migrations history (ADR 0010).
var trackerConnection = builder.Configuration.GetConnectionString("tracker")
    ?? throw new InvalidOperationException("ConnectionStrings:tracker is not set. See the README's dashboard setup.");
builder.Services.AddDbContext<ApplicationsDb>(o => o
    .UseNpgsql(trackerConnection, npgsql => npgsql.MigrationsHistoryTable(ApplicationsDb.HistoryTable))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton<TrackerMirror>();
builder.Services.AddSingleton<TrackerSchema>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IMarketSource, DbMarketSource>();
builder.Services.AddSingleton<MarketData>();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(MarketData.TelemetryName, TrackerTelemetry.Name))
    .WithMetrics(metrics => metrics.AddMeter(MarketData.TelemetryName, TrackerTelemetry.Name));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapMarketEndpoints();
app.MapTrackerEndpoints();
app.Run();

public partial class Program;
