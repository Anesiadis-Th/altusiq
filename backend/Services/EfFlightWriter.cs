using AltusIQ.Api.Data;
using AltusIQ.Api.Models;

namespace AltusIQ.Api.Services;

public class EfFlightWriter : IFlightWriter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EfFlightWriter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task WriteAsync(IReadOnlyList<Flight> flights, CancellationToken ct)
    {
        if (flights.Count == 0)
            return;

        // Singleton holding a scoped DbContext is the trap here, so scope per write.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();

        db.Flights.AddRange(flights);
        await db.SaveChangesAsync(ct);
    }
}
