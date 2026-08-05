using Microsoft.Extensions.Logging;

namespace DigitalBrain;

public sealed record CatalogFingerprint(string Value);

internal sealed partial class CatalogFingerprintAnnouncement(
    ILogger<CatalogFingerprintAnnouncement> logger,
    CatalogFingerprint fingerprint) : ILifecycleParticipant<ISiloLifecycle>
{
    public void Participate(ISiloLifecycle observer)
        => observer.Subscribe(
            nameof(CatalogFingerprintAnnouncement),
            ServiceLifecycleStage.ApplicationServices,
            _ =>
            {
                LogCatalogFingerprint(logger, fingerprint.Value);
                return Task.CompletedTask;
            });

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "DigitalBrain catalog fingerprint {CatalogFingerprint}; every silo of one brain must match")]
    private static partial void LogCatalogFingerprint(ILogger logger, string catalogFingerprint);
}
