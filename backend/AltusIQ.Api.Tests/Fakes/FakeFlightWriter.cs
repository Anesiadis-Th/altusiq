using AltusIQ.Api.Models;
using AltusIQ.Api.Services;

namespace AltusIQ.Api.Tests.Fakes;

internal sealed class FakeFlightWriter : IFlightWriter
{
    private readonly List<Flight> _persisted = [];

    public IReadOnlyList<Flight> Persisted => _persisted;
    public int WriteCallCount { get; private set; }

    /// <summary>Set to simulate the database being unavailable at flush time.</summary>
    public Exception? FailWith { get; set; }

    public Task WriteAsync(IReadOnlyList<Flight> flights, CancellationToken ct)
    {
        WriteCallCount++;

        if (FailWith is not null)
            throw FailWith;

        _persisted.AddRange(flights);
        return Task.CompletedTask;
    }

    public Flight Single() => Assert.Single(_persisted);
}
