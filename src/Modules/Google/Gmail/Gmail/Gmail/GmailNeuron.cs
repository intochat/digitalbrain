using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Timers;

namespace DigitalBrain.Google.Gmail;

[GrainType("gmail")]
internal sealed class GmailNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<GmailState> store) : Neuron, IGmail, IRemindable
{
    private const string WatchReminder = "watch";

    public async Task AcceptWatchPush(GmailWatchPush push)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(push.EmailAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(push.HistoryId);
        await PublishAsync(new MailReceived(push.EmailAddress, push.HistoryId)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (store.State is not { } connected
            || !string.Equals(this.GetPrimaryKeyString(), push.EmailAddress, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(connected.RefreshToken)
            || string.IsNullOrWhiteSpace(connected.HistoryId))
        {
            return;
        }

        var accessToken = await AccessTokenAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var added = await ServiceProvider.GetRequiredService<IGmailMailbox>()
            .ListAddedAsync(accessToken, connected.HistoryId, CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        foreach (var message in added)
        {
            await PublishAsync(new GmailMessageArrived(push.EmailAddress, message.MessageId, message.Subject))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        store.State = store.State with { HistoryId = push.HistoryId, AccessToken = null };
        await store.WriteStateAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
        GmailTokenRefresh.ValidateToken(grant.RefreshToken);
        var nonce = handoff.Deposit(new OAuthTokens(grant.AccessToken, grant.RefreshToken));
        if (!handoff.TryPeek(nonce, out _))
        {
            throw new TokenHandoffExpiredException();
        }

        handoff.Consume(nonce);
        var secret = new GmailMailboxSecret(email, ServiceProvider.GetRequiredService<GmailSecrets>().Protect(grant.RefreshToken!), grant.GrantedScopes ?? "");
        if (string.Equals(this.GetPrimaryKeyString(), email, StringComparison.OrdinalIgnoreCase))
        {
            await StoreMailboxSecret(secret).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await ArmWatch().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        else
        {
            var mailbox = GrainFactory.GetGrain<IGmail>(email);
            await mailbox.StoreMailboxSecret(secret).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await mailbox.ArmWatch().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await PublishAsync(new GmailConnected(email)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task ArmWatch()
    {
        var topic = ServiceProvider.GetRequiredService<GmailOAuthConfiguration>().TopicName;
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        if (!topic.StartsWith("projects/", StringComparison.Ordinal) || !topic.Contains("/topics/", StringComparison.Ordinal))
        {
            throw new GmailUnavailableException("Gmail Pub/Sub topic must be a projects/{project}/topics/{name} name.");
        }

        var accessToken = await AccessTokenAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var receipt = await ServiceProvider.GetRequiredService<IGmailMailbox>()
            .WatchAsync(accessToken, topic, CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        store.State = store.State with { HistoryId = receipt.HistoryId, WatchExpiresAt = receipt.Expiration, AccessToken = null };
        await store.WriteStateAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var reminders = ServiceProvider.GetService<IReminderRegistry>();
        if (reminders is not null)
        {
            await reminders.RegisterOrUpdateReminder(this.GetGrainId(), WatchReminder, TimeSpan.FromHours(24), TimeSpan.FromHours(24))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    public Task ReceiveReminder(string reminderName, TickStatus status)
        => reminderName == WatchReminder ? ArmWatch() : Task.CompletedTask;

    private async Task<string> AccessTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(store.State.RefreshToken))
        {
            throw new GmailNotConnectedException();
        }

        var refreshToken = ServiceProvider.GetRequiredService<GmailSecrets>().Unprotect(store.State.RefreshToken);
        var grant = await ServiceProvider.GetRequiredService<IGmailTokenExchange>()
            .ExchangeAsync(refreshToken, CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        GmailTokenRefresh.ValidateToken(grant.AccessToken);
        if (grant.RefreshToken is not null)
        {
            store.State = store.State with { RefreshToken = ServiceProvider.GetRequiredService<GmailSecrets>().Protect(grant.RefreshToken), AccessToken = null };
            await store.WriteStateAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        return grant.AccessToken;
    }

    public async Task StoreMailboxSecret(GmailMailboxSecret secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret.ProtectedRefreshToken);
        if (!string.Equals(this.GetPrimaryKeyString(), secret.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A mailbox secret belongs on the grain for that email.");
        }

        var current = store.State ?? new GmailState();
        store.State = current with { Email = secret.Email, RefreshToken = secret.ProtectedRefreshToken, GrantedScopes = secret.GrantedScopes ?? "", AccessToken = null };
        await store.WriteStateAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task<bool> IsMailboxConnected()
        => Task.FromResult(!string.IsNullOrWhiteSpace(store.State.RefreshToken));
}