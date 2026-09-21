using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Google;

[GrainType("gmail")]
public sealed class GmailNeuron : Neuron, IGmail
{
    public Task AcceptWatchPush(GmailWatchPush push)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(push.EmailAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(push.HistoryId);
        return PublishAsync(new MailReceived(push.EmailAddress, push.HistoryId));
    }

    public async Task AcceptAuthorizationCode(string authorizationCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);
        var tokens = ServiceProvider.GetRequiredService<IGmailTokenExchange>();
        var handoff = ServiceProvider.GetRequiredService<TokenHandoff>();
        var grant = await tokens.ExchangeAuthorizationCodeAsync(authorizationCode, CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        GmailTokenRefresh.ValidateToken(grant.AccessToken);
        var email = grant.Email ?? this.GetPrimaryKeyString();
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var nonce = handoff.Deposit(new OAuthTokens(grant.AccessToken, grant.RefreshToken));
        if (!handoff.TryPeek(nonce, out _))
        {
            throw new TokenHandoffExpiredException();
        }

        handoff.Consume(nonce);
        await PublishAsync(new GmailConnected(email)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}