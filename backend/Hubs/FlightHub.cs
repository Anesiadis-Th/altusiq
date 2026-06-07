using Microsoft.AspNetCore.SignalR;

namespace AltusIQ.Api.Hubs;

public class FlightHub : Hub
{
    private readonly ILogger<FlightHub> _logger;

    public FlightHub(ILogger<FlightHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}