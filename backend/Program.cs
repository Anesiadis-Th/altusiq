using Serilog;
using AltusIQ.Api.Background;
using AltusIQ.Api.Services;
using AltusIQ.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .WriteTo.Console());

// CORS
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

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    });

// SignalR
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    });

// Health checks
builder.Services.AddHealthChecks();

// Railway port binding
builder.WebHost.ConfigureKestrel(options =>
{
    var port = int.Parse(
        Environment.GetEnvironmentVariable("PORT") ?? "8080");
        
    options.ListenAnyIP(port);
});


// Typed HttpClient for OpenSky auth token requests
builder.Services.AddHttpClient<IOpenSkyAuthService, OpenSkyAuthService>();

// Singleton so the token cache persists for the app lifetime
builder.Services.AddSingleton<IOpenSkyAuthService, OpenSkyAuthService>();

// Typed HttpClient for OpenSky API calls from the polling service
builder.Services.AddHttpClient<FlightPollingService>();

// Register the background polling service
builder.Services.AddHostedService<FlightPollingService>();

var app = builder.Build();

app.UseCors("Frontend");

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<FlightHub>("/hubs/flights");

app.Run();