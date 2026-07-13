using System.Globalization;
using System.Net;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Runtime;
using Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RuntimeGmailMessageListRequest = DigitalBrain.Kernel.Runtime.GmailMessageListRequest;
using RuntimeGmailMessageListResult = DigitalBrain.Kernel.Runtime.GmailMessageListResult;
using RuntimeGmailMessageMetadata = DigitalBrain.Kernel.Runtime.GmailMessageMetadata;
using RuntimeGmailMessageSelection = DigitalBrain.Kernel.Runtime.GmailMessageSelection;
using RuntimeGmailResultCoverage = DigitalBrain.Kernel.Runtime.GmailResultCoverage;
using RuntimeGmailThreadListRequest = DigitalBrain.Kernel.Runtime.GmailThreadListRequest;
using RuntimeGmailThreadListResult = DigitalBrain.Kernel.Runtime.GmailThreadListResult;
using RuntimeGmailThreadMetadata = DigitalBrain.Kernel.Runtime.GmailThreadMetadata;

namespace DigitalBrain.Google;

[GrainType("digitalbrain.google.gmail-read")]
public sealed class GmailReadNeuron(
    ILogger<GmailReadNeuron> logger,
    IGmailApiClientFactory gmailApiClientFactory,
    IPackConfigStore store,
    [FromKeyedServices("google")] IConnector connector,
    IOAuthStateProtector oauthStateProtector) : Grain, IGmailReadToolGrain, IGmailMetadataToolGrain, IGmailMutationToolGrain
{
    private static readonly TimeSpan OAuthStartLifetime = TimeSpan.FromMinutes(5);

    public async Task<ExternalAuthorizationResolution> ResolveAuthorizationAsync(
        CancellationToken cancellationToken = default)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        var scope = new NeuronScope(new UserId(owner.Value), ThreadId: null);
        var userScope = PackConfigScopes.ForUser(scope.UserId);
        var pending = await store.GetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, cancellationToken);
        if (GoogleClientFactory.IsKnownPendingExpired(pending))
        {
            var compact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [GoogleClientFactory.OAuthPhaseKey] = GoogleClientFactory.OAuthPhaseFailed,
                [GoogleClientFactory.OAuthResultKey] = "expired"
            };
            if (pending.TryGetValue(GoogleClientFactory.OAuthFlowIdKey, out var flowId) &&
                GoogleClientFactory.IsAuthorizationFlowId(flowId))
                compact[GoogleClientFactory.OAuthFlowIdKey] = flowId;
            if (pending.TryGetValue(GoogleClientFactory.OAuthAttemptFingerprintKey, out var attempt) &&
                GoogleClientFactory.IsAuthorizationAttemptFingerprint(attempt))
                compact[GoogleClientFactory.OAuthAttemptFingerprintKey] = attempt;
            await store.SetAsync(
                userScope,
                GoogleClientFactory.OAuthPendingPackName,
                compact,
                cancellationToken);
            pending = compact;
        }
        var values = await GoogleClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        return GoogleClientFactory.ResolveAuthorization(values, pending);
    }

    public async Task<GmailReadResult> BeginAuthorizationAsync(
        string flowReference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = new NeuronId(this.GetPrimaryKeyString());
        if (!OAuthCallbackPaths.IsOpaqueFlowReference(flowReference) ||
            !oauthStateProtector.TryUnprotect(flowReference, out var protectedOwner) ||
            !string.Equals(protectedOwner.Value, owner.Value, StringComparison.Ordinal))
            return InvalidConnectionRequest();

        var userScope = PackConfigScopes.ForUser(new UserId(owner.Value));
        var pending = await store.GetAsync(
            userScope,
            GoogleClientFactory.OAuthPendingPackName,
            cancellationToken);
        if (!GoogleClientFactory.IsCurrentOAuthStartToken(pending, flowReference))
            return InvalidConnectionRequest();

        try
        {
            var challenge = await connector.BeginAuthAsync(owner, cancellationToken: cancellationToken);
            if (challenge.IsForm || !GoogleClientFactory.IsAllowedAuthorizationUrl(challenge.UrlOrForm))
                return new GmailReadResult(
                    GmailReadStatus.ConfigurationMissing,
                    SafeReason: "Gmail application configuration is missing.");

            return new GmailReadResult(
                GmailReadStatus.NeedsAuth,
                ConnectionUrl: challenge.UrlOrForm);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Google authorization start failed with {ExceptionType}.", ex.GetType().Name);
            return new GmailReadResult(
                GmailReadStatus.Unavailable,
                SafeReason: "Google connection is unavailable right now.");
        }
    }

    public Task<AuthResult> CompleteAuthorizationAsync(
        OAuthCallback callback,
        CancellationToken cancellationToken = default)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        if (!oauthStateProtector.TryUnprotect(callback.State, out var protectedOwner) ||
            !string.Equals(protectedOwner.Value, owner.Value, StringComparison.Ordinal))
            return Task.FromResult(new AuthResult(false, "invalid-state"));
        return connector.CompleteAuthAsync(callback, cancellationToken);
    }

    public async Task<GmailReadResult> ReadIncomingAtOffsetAsync(
        GmailReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        var scope = new NeuronScope(new UserId(owner.Value), ThreadId: null);
        var config = await connector.ValidateConfigAsync(cancellationToken: cancellationToken);
        if (!config.IsValid)
            return new GmailReadResult(
                GmailReadStatus.ConfigurationMissing,
                SafeReason: "Gmail application configuration is missing.");

        var values = await GoogleClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        if (!GoogleClientFactory.HasUsableCredential(values))
            return await BuildConnectionResultAsync(owner, values, cancellationToken);

        try
        {
            var client = await gmailApiClientFactory.CreateAsync(scope, cancellationToken);
            if (!Valid(request))
                return new GmailReadResult(
                    GmailReadStatus.Unavailable,
                    SafeReason: "That Gmail position cannot be read safely.");
            var latest = await client.ReadIncomingAtOffsetAsync(
                new GmailIncomingReadRequest(request.Offset, request.AnchorMessageId, request.AnchorInternalDate),
                cancellationToken);
            return latest.State switch
            {
                GmailLatestIncomingState.SenderAvailable => new GmailReadResult(
                    GmailReadStatus.Success,
                    Sender: latest.Sender,
                    SenderAddress: latest.SenderAddress,
                    MailboxState: GmailMailboxState.SenderAvailable,
                    MessageId: latest.MessageId,
                    InternalDate: latest.InternalDate,
                    TraversalDepth: request.TraversalDepth,
                    AnchoredPrevious: request.RequiresAnchor),
                GmailLatestIncomingState.EmptyInbox => new GmailReadResult(
                    GmailReadStatus.Success,
                    MailboxState: GmailMailboxState.EmptyInbox),
                GmailLatestIncomingState.PositionUnavailable => new GmailReadResult(
                    GmailReadStatus.Success,
                    MailboxState: GmailMailboxState.PositionUnavailable,
                    TraversalDepth: request.TraversalDepth,
                    AnchoredPrevious: request.RequiresAnchor),
                _ => new GmailReadResult(
                    GmailReadStatus.Success,
                    MailboxState: GmailMailboxState.SenderUnavailable,
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
                values,
                cancellationToken,
                "Google authorization does not include Gmail read permission. Reconnect Google and grant read access.");
        }
        catch (Exception ex) when (IsAuthorizationFailure(ex))
        {
            return await BuildConnectionResultAsync(
                owner,
                values,
                cancellationToken,
                "Google authorization expired or was revoked. Reconnect Google to continue.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Gmail read failed with {ExceptionType}.", ex.GetType().Name);
            return new GmailReadResult(
                GmailReadStatus.Unavailable,
                SafeReason: "I couldn’t read Gmail right now. Please try again later.");
        }
    }

    public Task<RuntimeGmailMessageListResult> ReadMessagesAsync(
        RuntimeGmailMessageListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Valid(request))
            return Task.FromResult(new RuntimeGmailMessageListResult(
                GmailReadStatus.Unavailable,
                [],
                EmptyCoverage(),
                "That Gmail message selection cannot be read safely."));

        return ExecuteMetadataReadAsync(
            async (client, token) =>
            {
                var result = await client.ListMessagesAsync(new GmailMessageListRequest(
                    Map(request.Selection), request.Offset, request.Limit), token);
                return new RuntimeGmailMessageListResult(
                    result.State == GmailMetadataReadState.Success
                        ? GmailReadStatus.Success
                        : GmailReadStatus.CapabilityUnavailable,
                    result.Messages.Select(Map).ToArray(),
                    Map(result.Coverage),
                    result.SafeReason,
                    StableCandidateMessageIds: result.StableCandidateMessageIds);
            },
            static (status, reason, url) => new RuntimeGmailMessageListResult(
                status, [], EmptyCoverage(), reason, url),
            cancellationToken);
    }

    public Task<GmailMailboxOverviewResult> ReadMailboxOverviewAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteMetadataReadAsync(
            async (client, token) =>
            {
                var result = await client.ReadMailboxOverviewAsync(token);
                return new GmailMailboxOverviewResult(
                    GmailReadStatus.Success,
                    result.InboxMessages,
                    result.UnreadInboxMessages,
                    result.InboxThreads,
                    result.UnreadInboxThreads);
            },
            static (status, reason, url) => new GmailMailboxOverviewResult(
                status, SafeReason: reason, ConnectionUrl: url),
            cancellationToken);

    public Task<RuntimeGmailThreadListResult> ReadThreadsAsync(
        RuntimeGmailThreadListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Valid(request))
            return Task.FromResult(new RuntimeGmailThreadListResult(
                GmailReadStatus.Unavailable,
                [],
                EmptyCoverage(),
                "That Gmail thread selection cannot be read safely."));

        return ExecuteMetadataReadAsync(
            async (client, token) =>
            {
                var result = await client.ListThreadsAsync(new GmailThreadListRequest(
                    Map(request.Selection), request.Offset, request.Limit, request.MaxMessagesPerThread), token);
                return new RuntimeGmailThreadListResult(
                    result.State == GmailMetadataReadState.Success
                        ? GmailReadStatus.Success
                        : GmailReadStatus.CapabilityUnavailable,
                    result.Threads.Select(thread => new RuntimeGmailThreadMetadata(
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
            static (status, reason, url) => new RuntimeGmailThreadListResult(
                status, [], EmptyCoverage(), reason, url),
            cancellationToken);
    }

    public async Task<GmailSendResult> SendAsync(
        GmailSendRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!GmailSendRequestValidator.IsValid(request))
            return new GmailSendResult(
                GmailSendStatus.InvalidRequest,
                SafeReason: "That Gmail message cannot be sent safely.");

        var owner = new NeuronId(this.GetPrimaryKeyString());
        var scope = new NeuronScope(new UserId(owner.Value), ThreadId: null);
        var config = await connector.ValidateConfigAsync(cancellationToken: cancellationToken);
        if (!config.IsValid)
            return new GmailSendResult(
                GmailSendStatus.ConfigurationMissing,
                SafeReason: "Gmail application configuration is missing.");

        var values = await GoogleClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        if (!GoogleClientFactory.HasUsableCredential(values))
            return await BuildMutationConnectionResultAsync(owner, values, cancellationToken);

        try
        {
            var client = await gmailApiClientFactory.CreateAsync(scope, cancellationToken);
            return await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            return await BuildMutationConnectionResultAsync(
                owner,
                values,
                cancellationToken,
                "Google authorization does not include Gmail send permission. Reconnect Google and grant send access.");
        }
        catch (Exception ex) when (IsAuthorizationFailure(ex))
        {
            return await BuildMutationConnectionResultAsync(
                owner,
                values,
                cancellationToken,
                "Google authorization expired or was revoked. Reconnect Google to continue.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Gmail send failed with {ExceptionType}.", ex.GetType().Name);
            return new GmailSendResult(
                GmailSendStatus.Unavailable,
                SafeReason: "I couldn’t send that Gmail message right now. Please try again later.");
        }
    }

    private async Task<T> ExecuteMetadataReadAsync<T>(
        Func<IGmailApiClient, CancellationToken, Task<T>> operation,
        Func<GmailReadStatus, string?, string?, T> failure,
        CancellationToken cancellationToken)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        var scope = new NeuronScope(new UserId(owner.Value), ThreadId: null);
        var config = await connector.ValidateConfigAsync(cancellationToken: cancellationToken);
        if (!config.IsValid)
            return failure(GmailReadStatus.ConfigurationMissing, "Gmail application configuration is missing.", null);

        var values = await GoogleClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        if (!GoogleClientFactory.HasUsableCredential(values))
        {
            var connection = await BuildConnectionResultAsync(owner, values, cancellationToken);
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
            var connection = await BuildConnectionResultAsync(owner, values, cancellationToken,
                "Google authorization does not include Gmail read permission. Reconnect Google and grant read access.");
            return failure(connection.Status, connection.SafeReason, connection.ConnectionUrl);
        }
        catch (Exception ex) when (IsAuthorizationFailure(ex))
        {
            var connection = await BuildConnectionResultAsync(owner, values, cancellationToken,
                "Google authorization expired or was revoked. Reconnect Google to continue.");
            return failure(connection.Status, connection.SafeReason, connection.ConnectionUrl);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Gmail metadata read failed with {ExceptionType}.", ex.GetType().Name);
            return failure(GmailReadStatus.Unavailable, "I couldn’t read Gmail right now. Please try again later.", null);
        }
    }

    private static GmailMessageSelection Map(RuntimeGmailMessageSelection selection) => new(
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

    private static RuntimeGmailMessageMetadata Map(GmailMessageMetadata message) => new(
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

    private static RuntimeGmailResultCoverage Map(GmailResultCoverage coverage) => new(
        coverage.PagesRead,
        coverage.CandidatesDiscovered,
        coverage.MetadataRead,
        coverage.MatchingMessages,
        coverage.UnavailableMessages,
        coverage.ProviderExhausted,
        coverage.CandidateLimitReached);

    private static RuntimeGmailResultCoverage EmptyCoverage() => new(0, 0, 0, 0, 0, true, false);

    private async Task<GmailReadResult> BuildConnectionResultAsync(
        NeuronId owner,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken,
        string reason = "Connect your Google account to let INO read your Gmail.")
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var userScope = PackConfigScopes.ForUser(new UserId(owner.Value));
            var pending = await store.GetAsync(
                userScope,
                GoogleClientFactory.OAuthPendingPackName,
                cancellationToken);
            var providerResolution = GoogleClientFactory.ResolveAuthorization(values, pending);
            if (pending.TryGetValue(GoogleClientFactory.OAuthPhaseKey, out var phase) &&
                string.Equals(phase, GoogleClientFactory.OAuthPhaseProcessing, StringComparison.Ordinal) &&
                providerResolution.State == ExternalAuthorizationResolutionState.Waiting)
                return new GmailReadResult(
                    GmailReadStatus.Unavailable,
                    SafeReason: "Google authorization is being completed. Please wait a moment.");

            if (TryGetReusableOAuthStartToken(pending, owner, out var reusableFlowReference))
                return new GmailReadResult(
                    GmailReadStatus.NeedsAuth,
                    SafeReason: reason,
                    ConnectionUrl: GoogleClientFactory.CreateOAuthStartUrl(reusableFlowReference));

            var flowReference = oauthStateProtector.Protect(owner);
            var preserveProviderChallenge =
                string.Equals(phase, GoogleClientFactory.OAuthPhaseChallengeIssued, StringComparison.Ordinal) &&
                providerResolution.State == ExternalAuthorizationResolutionState.Waiting;
            var nextPending = preserveProviderChallenge
                ? new Dictionary<string, string>(pending, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [GoogleClientFactory.OAuthPhaseKey] = GoogleClientFactory.OAuthPhaseLocalStart,
                    [GoogleClientFactory.OAuthFlowIdKey] = GoogleClientFactory.CreateAuthorizationFlowId()
                };
            nextPending[GoogleClientFactory.OAuthStartTokenKey] = flowReference;
            nextPending[GoogleClientFactory.OAuthStartTokenFingerprintKey] =
                GoogleClientFactory.AuthorizationAttemptFingerprint(flowReference);
            nextPending[GoogleClientFactory.OAuthStartExpiresAtKey] = DateTimeOffset.UtcNow
                .Add(OAuthStartLifetime)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
            await store.SetAsync(
                userScope,
                GoogleClientFactory.OAuthPendingPackName,
                nextPending,
                CancellationToken.None);

            return new GmailReadResult(
                GmailReadStatus.NeedsAuth,
                SafeReason: reason,
                ConnectionUrl: GoogleClientFactory.CreateOAuthStartUrl(flowReference));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Google connection link creation failed with {ExceptionType}.", ex.GetType().Name);
            return new GmailReadResult(
                GmailReadStatus.Unavailable,
                SafeReason: "Google connection is unavailable right now.");
        }
    }

    private async Task<GmailSendResult> BuildMutationConnectionResultAsync(
        NeuronId owner,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken,
        string reason = "Connect your Google account to let INO send Gmail messages.")
    {
        var connection = await BuildConnectionResultAsync(owner, values, cancellationToken, reason);
        return new GmailSendResult(
            connection.Status switch
            {
                GmailReadStatus.NeedsAuth => GmailSendStatus.NeedsAuth,
                GmailReadStatus.ConfigurationMissing => GmailSendStatus.ConfigurationMissing,
                _ => GmailSendStatus.Unavailable
            },
            SafeReason: connection.SafeReason,
            ConnectionUrl: connection.ConnectionUrl);
    }

    private static GmailReadResult InvalidConnectionRequest() => new(
        GmailReadStatus.Unavailable,
        SafeReason: "This Google connection request is invalid or expired. Start again from DigitalBrain.");

    private bool TryGetReusableOAuthStartToken(
        IReadOnlyDictionary<string, string> pending,
        NeuronId owner,
        out string flowReference)
    {
        flowReference = string.Empty;
        if (!GoogleClientFactory.TryGetCurrentOAuthStartToken(pending, out var candidate) ||
            !oauthStateProtector.TryUnprotect(candidate, out var protectedOwner) ||
            !string.Equals(protectedOwner.Value, owner.Value, StringComparison.Ordinal))
            return false;
        flowReference = candidate;
        return true;
    }

    private static bool Valid(GmailReadRequest request) =>
        request.Offset is >= 0 and <= GmailTools.MaximumOffset &&
        request.TraversalDepth is >= 0 and <= GmailTools.MaximumOffset &&
        (request.AnchorMessageId is null
            ? !request.RequiresAnchor && request.AnchorInternalDate is null && request.TraversalDepth == request.Offset
            : request.RequiresAnchor && request.Offset == 1 && request.AnchorInternalDate is not null &&
              request.AnchorMessageId.Length is > 0 and <= 256);

    private static bool Valid(RuntimeGmailMessageListRequest request) =>
        request.Selection is not null && Valid(request.Selection) &&
        request.Offset is >= 0 and < GmailTools.MaximumCandidateCount &&
        request.Limit is >= 1 and <= GmailTools.MaximumResultCount;

    private static bool Valid(RuntimeGmailThreadListRequest request) =>
        request.Selection is not null && Valid(request.Selection) &&
        request.Offset is >= 0 and < GmailTools.MaximumCandidateCount &&
        request.Limit is >= 1 and <= GmailTools.MaximumResultCount &&
        request.MaxMessagesPerThread is >= 1 and <= GmailTools.MaximumResultCount;

    private static bool Valid(RuntimeGmailMessageSelection selection) =>
        selection.MaxPages is >= 1 and <= GmailTools.MaximumPageCount &&
        selection.MaxCandidates is >= 1 and <= GmailTools.MaximumCandidateCount &&
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
