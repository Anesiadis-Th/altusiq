using Serilog;
using AltusIQ.Api.Background;
using AltusIQ.Api.Services;
using AltusIQ.Api.Hubs;
using AltusIQ.Api.Data;
using AltusIQ.Api.Models;
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

builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();

builder.Services.AddScoped<FlightQueryService>();
builder.Services.AddScoped<AnalyticsService>();

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
builder.Services.AddSingleton<FlightIngestionService>();
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

app.UseCors("Frontend");
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<FlightHub>("/hubs/flights");

app.Run();