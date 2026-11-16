using Serilog;
using AltusIQ.Api.Background;
using AltusIQ.Api.Services;
using AltusIQ.Api.Health;
using AltusIQ.Api.Hubs;
using AltusIQ.Api.Data;
using AltusIQ.Api.Investigation;
using AltusIQ.Api.Llm;
using AltusIQ.Api.Models;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .WriteTo.Console());

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origin = builder.Configuration["AllowedOrigin"]
            ?? "http://localhost:3000";

        policy.WithOrigins(origin)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    });



builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    });

builder.Services.AddSingleton<PollHeartbeat>();

builder.Services.AddHealthChecks()
    .AddCheck<PollerHealthCheck>("opensky_poller");

builder.Services.AddMemoryCache();

builder.Services.AddScoped<FlightQueryService>();
builder.Services.AddScoped<EventQueryService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddHostedService<AnalyticsCacheWarmer>();

builder.Services.AddHttpClient<IRouteLookupService, RouteLookupService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.WebHost.ConfigureKestrel(options =>
{
    var port = int.Parse(
        Environment.GetEnvironmentVariable("PORT") ?? "8080");
    options.ListenAnyIP(port);
});

builder.Services.AddHttpClient<OpenSkyAuthService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddSingleton<IOpenSkyAuthService>(sp =>
    sp.GetRequiredService<OpenSkyAuthService>());

builder.Services.Configure<IngestionSettings>(
    builder.Configuration.GetSection("Ingestion"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IFlightWriter, EfFlightWriter>();
builder.Services.AddSingleton<FlightIngestionService>();
builder.Services.AddSingleton<IActiveTrackSource>(sp =>
    sp.GetRequiredService<FlightIngestionService>());

builder.Services.Configure<EventsSettings>(
    builder.Configuration.GetSection("Events"));
builder.Services.AddSingleton<IAnomalyEventWriter, EfAnomalyEventWriter>();
builder.Services.AddSingleton<EmergencySquawkDetector>();

// Investigation: queue fed by the detector, drained by one background consumer.
// Model calls never happen on the poll path (ADR-018).
builder.Services.Configure<LlmSettings>(
    builder.Configuration.GetSection("Llm"));
builder.Services.AddSingleton<IInvestigationQueue, InvestigationQueue>();
builder.Services.AddSingleton<IAnomalyEventStore, EfAnomalyEventStore>();
builder.Services.AddSingleton<LlmCallBudget>();
builder.Services.AddSingleton<PromptStore>();
builder.Services.AddSingleton(_ => AirportIndex.LoadEmbedded());
builder.Services.AddHttpClient<MetarClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AltusIQ/1.0 (aviation analytics portfolio project)");
});
builder.Services.AddSingleton<IInvestigationToolSource, LiveToolSource>();
builder.Services.AddHttpClient<OpenAiCompatibleChatClient>((sp, client) =>
{
    var llm = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmSettings>>().Value;
    client.Timeout = TimeSpan.FromSeconds(llm.TimeoutSeconds);
});
builder.Services.AddSingleton<ILlmClient>(sp =>
    sp.GetRequiredService<OpenAiCompatibleChatClient>());
builder.Services.AddSingleton<InvestigationAgent>();
builder.Services.AddHostedService<InvestigationService>();
builder.Services.AddSingleton<LiveSnapshotStore>();

builder.Services.AddHttpClient<FlightPollingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<FlightPollingService>());

builder.Services.Configure<EnrichmentSettings>(
    builder.Configuration.GetSection("Enrichment"));

builder.Services.AddHttpClient<IOpenSkyFlightsClient, OpenSkyFlightsClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedService<FlightEnrichmentService>();

builder.Services.Configure<RetentionSettings>(
    builder.Configuration.GetSection("Retention"));

builder.Services.AddHostedService<RetentionService>();

var dataSourceBuilder = new NpgsqlDataSourceBuilder(
    builder.Configuration.GetConnectionString("DefaultConnection"));
dataSourceBuilder.EnableDynamicJson();
dataSourceBuilder.UseNetTopologySuite();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<AltusIqDbContext>(options =>
    options.UseNpgsql(dataSource, o => o.UseNetTopologySuite()));

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseCors("Frontend");

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHub<FlightHub>("/hubs/flights");

app.Run();