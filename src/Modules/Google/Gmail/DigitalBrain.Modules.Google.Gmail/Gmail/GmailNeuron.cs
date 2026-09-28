using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Sdk.Secrets;
using DigitalBrain.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Google.Gmail;

[GrainType("gmail")]
internal sealed class GmailNeuron(
    [PersistentState("gmail-vault", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<GmailState> store)
    : Neuron<GmailState>(store), IGmail
{
    public Task AcceptWatchPush(GmailWatchPush push)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(push.EmailAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(push.HistoryId);
        return PublishAsync(new MailReceived(push.EmailAddress, push.HistoryId));
    }

    public async Task AcceptAuthorizationCode(string authorizationCode, string? secretOwner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);
        var tokens = ServiceProvider.GetRequiredService<IGmailTokenExchange>();
        var grant = await tokens.ExchangeAuthorizationCodeAsync(authorizationCode, CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        GmailTokenPolicy.ValidateToken(grant.AccessToken);
        if (grant.RefreshToken is not null)
        {
            GmailTokenPolicy.ValidateToken(grant.RefreshToken);
        }

        var email = grant.Email ?? this.GetPrimaryKeyString();
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var owner = string.IsNullOrWhiteSpace(secretOwner) ? this.GetPrimaryKeyString() : secretOwner;
        var vault = GrainFactory.GetGrain<ISecrets>(owner);
        var platform = Platform();
        var credential = await vault.Set(platform, "gmail.access", "Gmail access token", grant.AccessToken, CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (!credential.IsSet)
        {
            throw new GmailNotConnectedException();
        }

        var refreshCredential = grant.RefreshToken is null
            ? null
            : await vault.Set(platform, "gmail.refresh", "Gmail refresh token", grant.RefreshToken, CancellationToken.None)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var expiresAt = GmailTokenPolicy.Expiry(grant.ExpiresInSeconds, ServiceProvider.GetRequiredService<TimeProvider>());
        await Save(Snapshot with
        {
            Email = email,
            Credential = credential,
            RefreshCredential = refreshCredential,
            GrantedScopes = grant.GrantedScopes ?? "",
            ExpiresAt = expiresAt,
        }, new GmailConnected(email)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    // The outbound call resolves the secret as trusted platform code, never as the user turn.
    private static CallerContext Platform() => new()
    {
        PrincipalId = "gmail",
        AccountId = "gmail",
        WorkspaceId = "gmail",
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        AppId = "gmail",
    };
}
