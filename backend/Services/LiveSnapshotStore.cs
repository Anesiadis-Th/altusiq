using AltusIQ.Api.Models;

namespace AltusIQ.Api.Services;

public class LiveSnapshotStore
{
    private IReadOnlyList<Aircraft> _aircraft = [];

    public IReadOnlyList<Aircraft> Aircraft => _aircraft;

    public void Update(IReadOnlyList<Aircraft> aircraft)
    {
        _aircraft = aircraft;
    }
}
