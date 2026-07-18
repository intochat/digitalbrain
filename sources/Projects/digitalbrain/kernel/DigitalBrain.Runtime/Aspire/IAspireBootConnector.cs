namespace DigitalBrain.Runtime.Aspire;

public interface IAspireBootConnector : IAsyncDisposable
{
    Task<string> SpawnClusterAsync(string profile, CancellationToken ct);

    Task<string> InstallDomainAsync(string domain, CancellationToken ct);

    Task<string> RestartResourceAsync(string resource, CancellationToken ct);

    Task<string> StartResourceAsync(string resource, CancellationToken ct);

    Task<string> StopResourceAsync(string resource, CancellationToken ct);

    Task WaitForShutdownAsync(CancellationToken ct);
}
