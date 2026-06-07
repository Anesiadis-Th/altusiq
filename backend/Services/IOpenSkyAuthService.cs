namespace AltusIQ.Api.Services;

public interface IOpenSkyAuthService
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
}