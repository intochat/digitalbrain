using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Platform.Contracts.Integrations;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed record GitHubAppCredentials(long AppId, string PrivateKeyPem);

internal sealed class GitHubAppRegistration(IGrainFactory grains)
{
    private const string RegistrationKey = "integration/github";
    private const string AppIdField = "AppId";
    private const string PrivateKeyField = "PrivateKeyPem";

    private static readonly CallerContext PlatformCaller = new()
    {
        PrincipalId = "github",
        AccountId = "github",
        BrainId = "github",
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        AppId = "github",
    };

    private IIntegrationRegistration Registration => grains.GetGrain<IIntegrationRegistration>(RegistrationKey);

    internal static long? AppIdOf(RegistrationSnapshot snapshot)
        => snapshot.Settings.TryGetValue(AppIdField, out var text) && long.TryParse(text, out var appId) && appId > 0 ? appId : null;

    internal async Task<long?> ReadAppIdAsync(CancellationToken cancellationToken)
    {
        var snapshot = await Registration.Read().WaitAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Status == RegistrationStatus.Ready ? AppIdOf(snapshot) : null;
    }

    internal async Task<GitHubAppCredentials> ReleaseAsync(CancellationToken cancellationToken)
    {
        var snapshot = await Registration.Read().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Status != RegistrationStatus.Ready || AppIdOf(snapshot) is not { } appId)
        {
            throw new GitHubUnavailableException("The GitHub App is not registered for this deployment.");
        }

        var released = await Registration.Release(PlatformCaller).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new GitHubAppCredentials(appId, released.Values[PrivateKeyField].Replace("\\n", "\n", StringComparison.Ordinal));
    }
}
