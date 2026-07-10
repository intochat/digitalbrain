using System.Net;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.V2;
using Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Google;

[GrainType("digitalbrain.google.gmail-read")]
public sealed class GmailReadNeuron(
    ILogger<GmailReadNeuron> logger,
    IGmailApiClientFactory gmailApiClientFactory,
    IPackConfigStore store,
    [FromKeyedServices("google")] IConnector connector) : Grain, IV2GmailReadToolGrain
{
    public async Task<V2GmailReadResult> ReadIncomingAtOffsetAsync(
        V2GmailReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        var scope = new NeuronScope(new UserId(owner.Value), ThreadId: null);
        var config = await connector.ValidateConfigAsync(cancellationToken: cancellationToken);
        if (!config.IsValid)
            return new V2GmailReadResult(
                V2GmailReadStatus.ConfigurationMissing,
                SafeReason: "Gmail application configuration is missing.");

        var values = await GoogleClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        if (!GoogleClientFactory.HasUsableCredential(values))
            return await BuildConnectionResultAsync(owner, cancellationToken);

        try
        {
            var client = await gmailApiClientFactory.CreateAsync(scope, cancellationToken);
            if (!Valid(request))
                return new V2GmailReadResult(
                    V2GmailReadStatus.Unavailable,
                    SafeReason: "That Gmail position cannot be read safely.");
            var latest = await client.ReadIncomingAtOffsetAsync(
                new GmailIncomingReadRequest(request.Offset, request.AnchorMessageId, request.AnchorInternalDate),
                cancellationToken);
            return latest.State switch
            {
                GmailLatestIncomingState.SenderAvailable => new V2GmailReadResult(
                    V2GmailReadStatus.Success,
                    Sender: latest.Sender,
                    SenderAddress: latest.SenderAddress,
                    MailboxState: V2GmailMailboxState.SenderAvailable,
                    MessageId: latest.MessageId,
                    InternalDate: latest.InternalDate,
                    TraversalDepth: request.TraversalDepth,
                    AnchoredPrevious: request.RequiresAnchor),
                GmailLatestIncomingState.EmptyInbox => new V2GmailReadResult(
                    V2GmailReadStatus.Success,
                    MailboxState: V2GmailMailboxState.EmptyInbox),
                GmailLatestIncomingState.PositionUnavailable => new V2GmailReadResult(
                    V2GmailReadStatus.Success,
                    MailboxState: V2GmailMailboxState.PositionUnavailable,
                    TraversalDepth: request.TraversalDepth,
                    AnchoredPrevious: request.RequiresAnchor),
                _ => new V2GmailReadResult(
                    V2GmailReadStatus.Success,
                    MailboxState: V2GmailMailboxState.SenderUnavailable,
                    MessageId: latest.MessageId,
                    InternalDate: latest.InternalDate,
                    TraversalDepth: request.TraversalDepth,
                    AnchoredPrevious: request.RequiresAnchor)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            return await BuildConnectionResultAsync(
                owner,
                cancellationToken,
                "Google authorization does not include Gmail read permission. Reconnect Google and grant read access.");
        }
        catch (Exception ex) when (IsAuthorizationFailure(ex))
        {
            return await BuildConnectionResultAsync(
                owner,
                cancellationToken,
                "Google authorization expired or was revoked. Reconnect Google to continue.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Gmail read failed with {ExceptionType}.", ex.GetType().Name);
            return new V2GmailReadResult(
                V2GmailReadStatus.Unavailable,
                SafeReason: "I couldn’t read Gmail right now. Please try again later.");
        }
    }

    private async Task<V2GmailReadResult> BuildConnectionResultAsync(
        NeuronId owner,
        CancellationToken cancellationToken,
        string reason = "Connect your Google account to let INO read your Gmail.")
    {
        var challenge = await connector.BeginAuthAsync(owner, cancellationToken: cancellationToken);
        if (challenge.IsForm || !IsAllowedGoogleAuthorizationUrl(challenge.UrlOrForm))
            return new V2GmailReadResult(
                V2GmailReadStatus.ConfigurationMissing,
                SafeReason: "Gmail application configuration is missing.");

        return new V2GmailReadResult(
            V2GmailReadStatus.NeedsAuth,
            SafeReason: reason,
            ConnectionUrl: challenge.UrlOrForm);
    }

    private static bool IsAllowedGoogleAuthorizationUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase);

    private static bool Valid(V2GmailReadRequest request) =>
        request.Offset is >= 0 and <= V2GmailTools.MaximumOffset &&
        request.TraversalDepth is >= 0 and <= V2GmailTools.MaximumOffset &&
        (request.AnchorMessageId is null
            ? !request.RequiresAnchor && request.AnchorInternalDate is null && request.TraversalDepth == request.Offset
            : request.RequiresAnchor && request.Offset == 1 && request.AnchorInternalDate is not null &&
              request.AnchorMessageId.Length is > 0 and <= 256);

    private static bool IsAuthorizationFailure(Exception exception)
    {
        if (exception is GoogleApiException google && google.HttpStatusCode == HttpStatusCode.Unauthorized) return true;
        var message = exception.GetBaseException().Message;
        return message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("revoked", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
    }
}
