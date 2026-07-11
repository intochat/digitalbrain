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
    [FromKeyedServices("google")] IConnector connector) : Grain, IV2GmailReadToolGrain, IV2GmailMetadataToolGrain
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

    public Task<V2GmailMessageListResult> ReadMessagesAsync(
        V2GmailMessageListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Valid(request))
            return Task.FromResult(new V2GmailMessageListResult(
                V2GmailReadStatus.Unavailable,
                [],
                EmptyCoverage(),
                "That Gmail message selection cannot be read safely."));

        return ExecuteMetadataReadAsync(
            async (client, token) =>
            {
                var result = await client.ListMessagesAsync(new GmailMessageListRequest(
                    Map(request.Selection), request.Offset, request.Limit), token);
                return new V2GmailMessageListResult(
                    result.State == GmailMetadataReadState.Success
                        ? V2GmailReadStatus.Success
                        : V2GmailReadStatus.CapabilityUnavailable,
                    result.Messages.Select(Map).ToArray(),
                    Map(result.Coverage),
                    result.SafeReason,
                    StableCandidateMessageIds: result.StableCandidateMessageIds);
            },
            static (status, reason, url) => new V2GmailMessageListResult(
                status, [], EmptyCoverage(), reason, url),
            cancellationToken);
    }

    public Task<V2GmailMailboxOverviewResult> ReadMailboxOverviewAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteMetadataReadAsync(
            async (client, token) =>
            {
                var result = await client.ReadMailboxOverviewAsync(token);
                return new V2GmailMailboxOverviewResult(
                    V2GmailReadStatus.Success,
                    result.InboxMessages,
                    result.UnreadInboxMessages,
                    result.InboxThreads,
                    result.UnreadInboxThreads);
            },
            static (status, reason, url) => new V2GmailMailboxOverviewResult(
                status, SafeReason: reason, ConnectionUrl: url),
            cancellationToken);

    public Task<V2GmailThreadListResult> ReadThreadsAsync(
        V2GmailThreadListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Valid(request))
            return Task.FromResult(new V2GmailThreadListResult(
                V2GmailReadStatus.Unavailable,
                [],
                EmptyCoverage(),
                "That Gmail thread selection cannot be read safely."));

        return ExecuteMetadataReadAsync(
            async (client, token) =>
            {
                var result = await client.ListThreadsAsync(new GmailThreadListRequest(
                    Map(request.Selection), request.Offset, request.Limit, request.MaxMessagesPerThread), token);
                return new V2GmailThreadListResult(
                    result.State == GmailMetadataReadState.Success
                        ? V2GmailReadStatus.Success
                        : V2GmailReadStatus.CapabilityUnavailable,
                    result.Threads.Select(thread => new V2GmailThreadMetadata(
                        thread.ThreadId,
                        thread.LatestInternalDate,
                        thread.Subject,
                        thread.ParticipantAddresses,
                        thread.HasUnread,
                        thread.MatchingMessageCount,
                        thread.Messages.Select(Map).ToArray())).ToArray(),
                    Map(result.Coverage),
                    result.SafeReason,
                    StableCandidateMessageIds: result.StableCandidateMessageIds,
                    StableCandidateThreadIds: result.StableCandidateThreadIds);
            },
            static (status, reason, url) => new V2GmailThreadListResult(
                status, [], EmptyCoverage(), reason, url),
            cancellationToken);
    }

    private async Task<T> ExecuteMetadataReadAsync<T>(
        Func<IGmailApiClient, CancellationToken, Task<T>> operation,
        Func<V2GmailReadStatus, string?, string?, T> failure,
        CancellationToken cancellationToken)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        var scope = new NeuronScope(new UserId(owner.Value), ThreadId: null);
        var config = await connector.ValidateConfigAsync(cancellationToken: cancellationToken);
        if (!config.IsValid)
            return failure(V2GmailReadStatus.ConfigurationMissing, "Gmail application configuration is missing.", null);

        var values = await GoogleClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        if (!GoogleClientFactory.HasUsableCredential(values))
        {
            var connection = await BuildConnectionResultAsync(owner, cancellationToken);
            return failure(connection.Status, connection.SafeReason, connection.ConnectionUrl);
        }

        try
        {
            var client = await gmailApiClientFactory.CreateAsync(scope, cancellationToken);
            return await operation(client, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            var connection = await BuildConnectionResultAsync(owner, cancellationToken,
                "Google authorization does not include Gmail read permission. Reconnect Google and grant read access.");
            return failure(connection.Status, connection.SafeReason, connection.ConnectionUrl);
        }
        catch (Exception ex) when (IsAuthorizationFailure(ex))
        {
            var connection = await BuildConnectionResultAsync(owner, cancellationToken,
                "Google authorization expired or was revoked. Reconnect Google to continue.");
            return failure(connection.Status, connection.SafeReason, connection.ConnectionUrl);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Gmail metadata read failed with {ExceptionType}.", ex.GetType().Name);
            return failure(V2GmailReadStatus.Unavailable, "I couldn’t read Gmail right now. Please try again later.", null);
        }
    }

    private static GmailMessageSelection Map(V2GmailMessageSelection selection) => new(
        (GmailMailboxScope)selection.Mailbox,
        (GmailMessageReadState)selection.ReadState,
        selection.SenderAddress,
        selection.RecipientAddress,
        selection.SubjectContains,
        selection.ReceivedAfterInclusive,
        selection.ReceivedBeforeExclusive,
        (GmailAttachmentFilter)selection.AttachmentFilter,
        selection.PinnedMessageIds,
        selection.MaxPages,
        selection.MaxCandidates);

    private static V2GmailMessageMetadata Map(GmailMessageMetadata message) => new(
        message.MessageId,
        message.ThreadId,
        message.InternalDate,
        message.From,
        message.FromAddress,
        message.To,
        message.ToAddresses,
        message.Subject,
        message.LabelIds,
        message.IsRead);

    private static V2GmailResultCoverage Map(GmailResultCoverage coverage) => new(
        coverage.PagesRead,
        coverage.CandidatesDiscovered,
        coverage.MetadataRead,
        coverage.MatchingMessages,
        coverage.UnavailableMessages,
        coverage.ProviderExhausted,
        coverage.CandidateLimitReached);

    private static V2GmailResultCoverage EmptyCoverage() => new(0, 0, 0, 0, 0, true, false);

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

    private static bool Valid(V2GmailMessageListRequest request) =>
        request.Selection is not null && Valid(request.Selection) &&
        request.Offset is >= 0 and < V2GmailTools.MaximumCandidateCount &&
        request.Limit is >= 1 and <= V2GmailTools.MaximumResultCount;

    private static bool Valid(V2GmailThreadListRequest request) =>
        request.Selection is not null && Valid(request.Selection) &&
        request.Offset is >= 0 and < V2GmailTools.MaximumCandidateCount &&
        request.Limit is >= 1 and <= V2GmailTools.MaximumResultCount &&
        request.MaxMessagesPerThread is >= 1 and <= V2GmailTools.MaximumResultCount;

    private static bool Valid(V2GmailMessageSelection selection) =>
        selection.MaxPages is >= 1 and <= V2GmailTools.MaximumPageCount &&
        selection.MaxCandidates is >= 1 and <= V2GmailTools.MaximumCandidateCount &&
        selection.SenderAddress is not { Length: > 320 } &&
        selection.RecipientAddress is not { Length: > 320 } &&
        selection.SubjectContains is not { Length: > 256 } &&
        selection.PinnedMessageIds is not { Length: 0 } &&
        (selection.PinnedMessageIds is null || selection.PinnedMessageIds.Length <= selection.MaxCandidates) &&
        selection.ReceivedAfterInclusive is not < 0 && selection.ReceivedBeforeExclusive is not < 0 &&
        !(selection.ReceivedAfterInclusive >= selection.ReceivedBeforeExclusive);

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
