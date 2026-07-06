using AltusIQ.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace AltusIQ.Api.Hubs;

public class FlightHub : Hub
{
    private readonly ILogger<FlightHub> _logger;
    private readonly LiveSnapshotStore _snapshotStore;

    public FlightHub(ILogger<FlightHub> logger, LiveSnapshotStore snapshotStore)
    {
        _logger = logger;
        _snapshotStore = snapshotStore;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "Client connected: {ConnectionId}", Context.ConnectionId);

        var snapshot = _snapshotStore.Aircraft;
        if (snapshot.Count > 0)
        {
            await Clients.Caller.SendAsync("ReceiveFlightData", snapshot);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}