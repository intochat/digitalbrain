using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Platform.Contracts.Integrations;

namespace DigitalBrain.Google.Gmail;

internal sealed record GmailOAuthCredentials(string ClientId, string ClientSecret, Uri PublicOrigin);

internal static class GmailScopes
{
    internal const string Read = "https://www.googleapis.com/auth/gmail.readonly";
}

internal sealed class GmailRegistration(IGrainFactory grains)
{
    private const string RegistrationKey = "integration/gmail";
    private const string PublicOriginField = "PublicOrigin";

    private static readonly CallerContext PlatformCaller = new()
    {
        PrincipalId = "gmail",
        AccountId = "gmail",
        BrainId = "gmail",
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        AppId = "gmail",
    };

    private IIntegrationRegistration Registration => grains.GetGrain<IIntegrationRegistration>(RegistrationKey);

    internal Task<RegistrationSnapshot> ReadAsync() => Registration.Read();

    internal static Uri? PublicOriginOf(RegistrationSnapshot snapshot)
        => snapshot.Settings.TryGetValue(PublicOriginField, out var text)
            && Uri.TryCreate(text, UriKind.Absolute, out var origin)
            && (origin.Scheme == "https" || origin.Scheme == "http" && origin.IsLoopback)
            && origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.Fragment.Length == 0
            && origin.UserInfo.Length == 0
                ? origin
                : null;

    // The explanation a caller can act on; null when Gmail can start a login.
    internal static GmailUnavailability? Explain(RegistrationSnapshot snapshot)
    {
        if (snapshot.Status != RegistrationStatus.Ready)
        {
            return new GmailUnavailability("gmail", snapshot.Status.ToString(), snapshot.MissingFields);
        }

        return PublicOriginOf(snapshot) is null ? new GmailUnavailability("gmail", "Invalid", [PublicOriginField]) : null;
    }

    internal async Task<GmailOAuthCredentials> ReleaseAsync()
    {
        var snapshot = await ReadAsync().ConfigureAwait(false);
        if (Explain(snapshot) is not null)
        {
            throw new GmailUnavailableException("Gmail is not configured for this deployment.");
        }

        var released = await Registration.Release(PlatformCaller).ConfigureAwait(false);
        return new GmailOAuthCredentials(released.Values["ClientId"], released.Values["ClientSecret"], PublicOriginOf(snapshot)!);
    }
}

internal sealed record GmailUnavailability(string Integration, string Status, string[] Missing);
