using Serilog;
using AltusIQ.Api.Background;
using AltusIQ.Api.Services;
using AltusIQ.Api.Hubs;

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

builder.WebHost.ConfigureKestrel(options =>
{
    var port = int.Parse(
        Environment.GetEnvironmentVariable("PORT") ?? "8080");
    options.ListenAnyIP(port);
});

builder.Services.AddHttpClient<IOpenSkyAuthService, OpenSkyAuthService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddSingleton<IOpenSkyAuthService, OpenSkyAuthService>();

builder.Services.AddHttpClient<FlightPollingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedService<FlightPollingService>();

var app = builder.Build();

app.UseCors("Frontend");
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<FlightHub>("/hubs/flights");

app.Run();