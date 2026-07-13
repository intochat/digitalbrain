using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProviderSalesforceContinuation = DigitalBrain.Salesforce.SalesforceContinuation;
using RuntimeSalesforceContinuation = DigitalBrain.Kernel.Runtime.SalesforceContinuation;

namespace DigitalBrain.Salesforce;

[GrainType("digitalbrain.salesforce.account-read")]
public sealed class SalesforceReadNeuron(
    ILogger<SalesforceReadNeuron> logger,
    ISalesforceApiClientFactory salesforceApiClientFactory,
    IPackConfigStore store,
    [FromKeyedServices("salesforce")] IConnector connector,
    IOAuthStateProtector oauthStateProtector,
    [PersistentState("salesforce-read", "Default")] IPersistentState<SalesforceReadNeuronState> continuationState)
    : Grain, ISalesforceReadToolGrain
{
    private const int MaximumContinuations = 32;
    private static readonly TimeSpan OAuthStartLifetime = TimeSpan.FromMinutes(5);

    public async Task<ExternalAuthorizationResolution> ResolveAuthorizationAsync(
        CancellationToken cancellationToken = default)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        var scope = new NeuronScope(new UserId(owner.Value), ThreadId: null);
        var userScope = PackConfigScopes.ForUser(scope.UserId);
        var pending = await store.GetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, cancellationToken);
        var values = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        var providerResolution = SalesforceClientFactory.ResolveAuthorization(values, pending);
        if (SalesforceClientFactory.IsProviderAuthorizationPhase(pending))
            return providerResolution;
        if (TryResolvePersistedOAuthStart(pending, out var persistedStart))
            return persistedStart;

        var startTokenHash = continuationState.State.OAuthStartTokenHash;
        if (startTokenHash is { Length: 32 })
        {
            if (providerResolution.State == ExternalAuthorizationResolutionState.Ready)
                return providerResolution;
            if (continuationState.State.OAuthStartExpiresAtUnixSeconds >= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return new(ExternalAuthorizationResolutionState.Waiting);
            if (pending.Count == 0)
                return new(ExternalAuthorizationResolutionState.Failed, "authorization-start-expired");
        }
        return providerResolution;
    }

    public async Task<SalesforceReadResult> BeginAuthorizationAsync(
        string startToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = new NeuronId(this.GetPrimaryKeyString());
        if (!oauthStateProtector.TryUnprotect(startToken, out var protectedOwner) ||
            !string.Equals(protectedOwner.Value, owner.Value, StringComparison.Ordinal))
        {
            return new SalesforceReadResult(
                SalesforceReadStatus.Unavailable,
                SafeReason: "This Salesforce connection request is invalid or expired. Start again from DigitalBrain.");
        }

        var userScope = PackConfigScopes.ForUser(new UserId(owner.Value));
        var pending = await store.GetAsync(
            userScope,
            SalesforceClientFactory.OAuthPendingPackName,
            cancellationToken);
        var isPersistedStart = IsCurrentPersistedOAuthStartToken(pending, startToken);
        var isLegacyStart = IsCurrentLegacyOAuthStartToken(startToken);
        if (!isPersistedStart && !isLegacyStart)
        {
            return new SalesforceReadResult(
                SalesforceReadStatus.Unavailable,
                SafeReason: "This Salesforce connection request is invalid or expired. Start again from DigitalBrain.");
        }

        try
        {
            var challenge = await connector.BeginAuthAsync(owner, cancellationToken: cancellationToken);
            if (challenge.IsForm || !SalesforceClientFactory.IsAllowedAuthorizationUrl(challenge.UrlOrForm))
            {
                if (isPersistedStart)
                    await TerminalizePersistedOAuthStartAsync(userScope, pending, "configuration-missing");
                return new SalesforceReadResult(
                    SalesforceReadStatus.ConfigurationMissing,
                    SafeReason: "Salesforce application configuration is missing.");
            }
            if (isLegacyStart)
                await ConsumeLegacyOAuthStartTokenAsync();

            return new SalesforceReadResult(
                SalesforceReadStatus.NeedsAuth,
                ConnectionUrl: challenge.UrlOrForm);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Salesforce authorization start failed with {ExceptionType}.", ex.GetType().Name);
            return new SalesforceReadResult(
                SalesforceReadStatus.Unavailable,
                SafeReason: "Salesforce connection is unavailable right now.");
        }
    }

    public Task<AuthResult> CompleteAuthorizationAsync(
        OAuthCallback callback,
        CancellationToken cancellationToken = default)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        if (!oauthStateProtector.TryUnprotect(callback.State, out var protectedOwner) ||
            !string.Equals(protectedOwner.Value, owner.Value, StringComparison.Ordinal))
        {
            return Task.FromResult(new AuthResult(false, "invalid-state"));
        }

        return connector.CompleteAuthAsync(callback, cancellationToken);
    }

    public Task<SalesforceReadResult> ReadLatestAccountAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(
            async (client, ct) =>
            {
                var accounts = await client.ListAccountsAsync(1, ct);
                return accounts.Length == 0 ? "No Salesforce accounts were found." : accounts[0];
            },
            cancellationToken);

    public Task<SalesforceReadResult> ReadCurrentProfileAsync(CancellationToken cancellationToken = default) =>
        ReadAsync((client, ct) => client.GetCurrentUserProfileAsync(ct), cancellationToken);

    public Task<SalesforceReadResult> ReadRecentAccountsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(
            async (client, ct) =>
            {
                var accounts = await client.ListAccountsAsync(10, ct);
                return accounts.Length == 0 ? "No Salesforce accounts were found." : "[" + string.Join(',', accounts) + "]";
            },
            cancellationToken);

    public Task<SalesforceReadResult> ReadRecentContactsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(
            async (client, ct) =>
            {
                var contacts = await client.ListContactsAsync(10, ct);
                return contacts.Length == 0 ? "No Salesforce contacts were found." : "[" + string.Join(',', contacts) + "]";
            },
            cancellationToken);

    public Task<SalesforceReadResult> ReadCrmSchemaAsync(CancellationToken cancellationToken = default) =>
        ReadAsync((client, ct) => client.DescribeCrmAccessAsync(ct), cancellationToken);

    public Task<SalesforceReadResult> DiscoverObjectsAsync(
        SalesforceDiscoveryRequest request,
        CancellationToken cancellationToken = default) =>
        ReadPageAsync((client, ct) => client.DiscoverObjectsAsync(request, ct), cancellationToken);

    public Task<SalesforceReadResult> ReadRecordsAsync(
        SalesforceRecordReadRequest request,
        CancellationToken cancellationToken = default) =>
        ReadPageAsync((client, ct) => client.ReadRecordsAsync(request, ct), cancellationToken);

    public Task<SalesforceReadResult> SearchRecordsAsync(
        SalesforceSearchRequest request,
        CancellationToken cancellationToken = default) =>
        ReadPageAsync((client, ct) => client.SearchRecordsAsync(request, ct), cancellationToken);

    public Task<SalesforceReadResult> AggregateRecordsAsync(
        SalesforceAggregateRequest request,
        CancellationToken cancellationToken = default) =>
        ReadPageAsync((client, ct) => client.AggregateRecordsAsync(request, ct), cancellationToken);

    public Task<SalesforceReadResult> ContinueRecordsAsync(
        SalesforceContinuationRequest request,
        CancellationToken cancellationToken = default) =>
        ReadPageAsync(
            async (client, ct) =>
            {
                var stored = string.IsNullOrWhiteSpace(request.Value)
                    ? null
                    : ReadStoredContinuations().FirstOrDefault(item =>
                        string.Equals(item.Token, request.Value, StringComparison.Ordinal));
                if (stored is null)
                    throw new SalesforceReadException(
                        SalesforceReadFailure.ContinuationExpired,
                        "That Salesforce continuation is no longer available.");
                return await client.ContinueRecordsAsync(stored.ToProviderContinuation(), ct);
            },
            cancellationToken,
            request.Value);

    private async Task<SalesforceReadResult> ReadPageAsync(
        Func<ISalesforceApiClient, CancellationToken, Task<SalesforceReadPage>> read,
        CancellationToken cancellationToken,
        string? continuationToConsume = null)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        var scope = new NeuronScope(new UserId(owner.Value), ThreadId: null);
        var config = await connector.ValidateConfigAsync(
            PackConfigScopes.ForUser(scope.UserId),
            cancellationToken);
        if (!config.IsValid)
            return new SalesforceReadResult(
                SalesforceReadStatus.ConfigurationMissing,
                SafeReason: "Salesforce application configuration is missing.");

        var values = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        if (!SalesforceClientFactory.HasUsableCredential(values))
            return await BuildConnectionResultAsync(owner, values, cancellationToken);

        try
        {
            var client = await salesforceApiClientFactory.CreateAsync(scope, cancellationToken);
            var page = await read(client, cancellationToken);
            var publicContinuation = await PersistContinuationUpdateAsync(
                continuationToConsume,
                page.Continuation,
                owner);

            return new SalesforceReadResult(
                SalesforceReadStatus.Success,
                page.Content,
                Scope: new SalesforceReadScope(
                    owner.Value,
                    page.Scope.OrganizationId,
                    page.Scope.SalesforceUserId),
                Continuation: publicContinuation,
                ReturnedCount: page.ReturnedCount,
                TotalSize: page.TotalSize);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SalesforceReadException ex)
        {
            return new SalesforceReadResult(
                ex.Failure switch
                {
                    SalesforceReadFailure.InvalidRequest => SalesforceReadStatus.InvalidRequest,
                    SalesforceReadFailure.AccessDenied => SalesforceReadStatus.AccessDenied,
                    SalesforceReadFailure.LimitReached => SalesforceReadStatus.LimitReached,
                    SalesforceReadFailure.ContinuationExpired => SalesforceReadStatus.ContinuationExpired,
                    _ => SalesforceReadStatus.Unavailable
                },
                SafeReason: ex.Message);
        }
        catch (Exception ex) when (IsPermissionFailure(ex))
        {
            return new SalesforceReadResult(
                SalesforceReadStatus.AccessDenied,
                SafeReason: "Salesforce authorization does not include the required read permission.");
        }
        catch (Exception ex) when (IsAuthorizationFailure(ex))
        {
            return await BuildConnectionResultAsync(
                owner,
                values,
                cancellationToken,
                "Salesforce authorization expired or was revoked. Reconnect Salesforce to continue.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Salesforce semantic read failed with {ExceptionType}.", ex.GetType().Name);
            return new SalesforceReadResult(
                SalesforceReadStatus.Unavailable,
                SafeReason: "I couldn’t read Salesforce right now. Please try again later.");
        }
    }

    private async Task<RuntimeSalesforceContinuation?> PersistContinuationUpdateAsync(
        string? continuationToConsume,
        ProviderSalesforceContinuation? providerContinuation,
        NeuronId owner)
    {
        if (string.IsNullOrWhiteSpace(continuationToConsume) && providerContinuation is null)
            return null;

        var continuations = ReadStoredContinuations()
            .Where(item => !string.Equals(item.Token, continuationToConsume, StringComparison.Ordinal))
            .ToList();
        RuntimeSalesforceContinuation? publicContinuation = null;
        if (providerContinuation is not null)
        {
            if (continuations.Count >= MaximumContinuations)
                continuations.RemoveAt(0);
            var token = Guid.NewGuid().ToString("N");
            continuations.Add(SalesforceStoredContinuation.From(token, providerContinuation));
            publicContinuation = new RuntimeSalesforceContinuation(
                token,
                owner.Value,
                providerContinuation.Scope.OrganizationId);
        }

        var previousState = continuationState.State;
        continuationState.State = new SalesforceReadNeuronState
        {
            SerializedContinuations = SalesforceContinuationStateCodec.Encode(continuations),
            OAuthStartTokenHash = previousState.OAuthStartTokenHash,
            OAuthStartExpiresAtUnixSeconds = previousState.OAuthStartExpiresAtUnixSeconds
        };
        try
        {
            await continuationState.WriteStateAsync(CancellationToken.None);
            return publicContinuation;
        }
        catch
        {
            continuationState.State = previousState;
            throw;
        }
    }

    private IReadOnlyList<SalesforceStoredContinuation> ReadStoredContinuations()
    {
        if (SalesforceContinuationStateCodec.TryDecode(
                continuationState.State.SerializedContinuations,
                out var continuations))
            return continuations;
        logger.LogWarning("Principal-scoped Salesforce continuation state was invalid and will be ignored.");
        return [];
    }

    private async Task<SalesforceReadResult> ReadAsync(
        Func<ISalesforceApiClient, CancellationToken, Task<string>> read,
        CancellationToken cancellationToken)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        var scope = new NeuronScope(new UserId(owner.Value), ThreadId: null);
        var config = await connector.ValidateConfigAsync(
            PackConfigScopes.ForUser(scope.UserId),
            cancellationToken);
        if (!config.IsValid)
            return new SalesforceReadResult(
                SalesforceReadStatus.ConfigurationMissing,
                SafeReason: "Salesforce application configuration is missing.");

        var values = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        if (!SalesforceClientFactory.HasUsableCredential(values))
            return await BuildConnectionResultAsync(owner, values, cancellationToken);

        try
        {
            var client = await salesforceApiClientFactory.CreateAsync(scope, cancellationToken);
            var content = await read(client, cancellationToken);
            return new SalesforceReadResult(
                SalesforceReadStatus.Success,
                content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsPermissionFailure(ex))
        {
            return await BuildConnectionResultAsync(
                owner,
                values,
                cancellationToken,
                "Salesforce authorization does not include the required read permission. Reconnect Salesforce and grant API access.");
        }
        catch (Exception ex) when (IsAuthorizationFailure(ex))
        {
            return await BuildConnectionResultAsync(
                owner,
                values,
                cancellationToken,
                "Salesforce authorization expired or was revoked. Reconnect Salesforce to continue.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Salesforce read failed with {ExceptionType}.", ex.GetType().Name);
            return new SalesforceReadResult(
                SalesforceReadStatus.Unavailable,
                SafeReason: "I couldn’t read Salesforce right now. Please try again later.");
        }
    }

    private async Task<SalesforceReadResult> BuildConnectionResultAsync(
        NeuronId owner,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken,
        string reason = "Connect your Salesforce account to let INO read Salesforce.")
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var userScope = PackConfigScopes.ForUser(new UserId(owner.Value));
            var pending = await store.GetAsync(
                userScope,
                SalesforceClientFactory.OAuthPendingPackName,
                cancellationToken);
            var providerResolution = SalesforceClientFactory.ResolveAuthorization(values, pending);

            if (TryGetReusableLocalStartToken(pending, owner, out var reusableStartToken))
            {
                return new SalesforceReadResult(
                    SalesforceReadStatus.NeedsAuth,
                    SafeReason: reason,
                    ConnectionUrl: SalesforceClientFactory.CreateOAuthStartUrl(reusableStartToken));
            }

            if (pending.TryGetValue(SalesforceClientFactory.OAuthPhaseKey, out var phase) &&
                string.Equals(phase, SalesforceClientFactory.OAuthPhaseProcessing, StringComparison.Ordinal) &&
                providerResolution.State == ExternalAuthorizationResolutionState.Waiting)
            {
                return new SalesforceReadResult(
                    SalesforceReadStatus.Unavailable,
                    SafeReason: "Salesforce authorization is being completed. Please wait a moment.");
            }

            var startToken = oauthStateProtector.Protect(owner);
            var startUrl = SalesforceClientFactory.CreateOAuthStartUrl(startToken);
            Dictionary<string, string> nextPending;
            var startsNewFlow = false;
            if (pending.TryGetValue(SalesforceClientFactory.OAuthPhaseKey, out phase) &&
                string.Equals(phase, SalesforceClientFactory.OAuthPhaseChallengeIssued, StringComparison.Ordinal) &&
                providerResolution.State == ExternalAuthorizationResolutionState.Waiting)
            {
                nextPending = new Dictionary<string, string>(pending, StringComparer.OrdinalIgnoreCase);
                if (!nextPending.TryGetValue(SalesforceClientFactory.OAuthFlowIdKey, out var flowId) ||
                    !SalesforceClientFactory.IsAuthorizationFlowId(flowId))
                    nextPending[SalesforceClientFactory.OAuthFlowIdKey] = SalesforceClientFactory.CreateAuthorizationFlowId();
            }
            else
            {
                startsNewFlow = true;
                nextPending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [SalesforceClientFactory.OAuthPhaseKey] = SalesforceClientFactory.OAuthPhaseLocalStart,
                    [SalesforceClientFactory.OAuthFlowIdKey] = SalesforceClientFactory.CreateAuthorizationFlowId()
                };
            }

            nextPending[SalesforceClientFactory.OAuthStartTokenKey] = startToken;
            nextPending[SalesforceClientFactory.OAuthStartTokenFingerprintKey] =
                SalesforceClientFactory.AuthorizationAttemptFingerprint(startToken);
            nextPending[SalesforceClientFactory.OAuthStartExpiresAtKey] = DateTimeOffset.UtcNow
                .Add(OAuthStartLifetime)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
            if (startsNewFlow)
            {
                await store.SetAsync(
                    userScope,
                    SalesforceClientFactory.PackName,
                    new Dictionary<string, string>(),
                    CancellationToken.None);
            }
            await store.SetAsync(
                userScope,
                SalesforceClientFactory.OAuthPendingPackName,
                nextPending,
                CancellationToken.None);

            return new SalesforceReadResult(
                SalesforceReadStatus.NeedsAuth,
                SafeReason: reason,
                ConnectionUrl: startUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Salesforce connection link creation failed with {ExceptionType}.", ex.GetType().Name);
            return new SalesforceReadResult(
                SalesforceReadStatus.Unavailable,
                SafeReason: "Salesforce connection is unavailable right now.");
        }
    }

    private static bool TryResolvePersistedOAuthStart(
        IReadOnlyDictionary<string, string> pending,
        out ExternalAuthorizationResolution resolution)
    {
        if (!pending.TryGetValue(SalesforceClientFactory.OAuthPhaseKey, out var phase) ||
            !string.Equals(phase, SalesforceClientFactory.OAuthPhaseLocalStart, StringComparison.Ordinal))
        {
            resolution = default!;
            return false;
        }

        if (!pending.TryGetValue(SalesforceClientFactory.OAuthStartTokenKey, out var startToken) ||
            string.IsNullOrWhiteSpace(startToken) ||
            !pending.TryGetValue(SalesforceClientFactory.OAuthStartTokenFingerprintKey, out var fingerprint) ||
            !SalesforceClientFactory.SameAuthorizationAttempt(
                fingerprint,
                SalesforceClientFactory.AuthorizationAttemptFingerprint(startToken)) ||
            !pending.TryGetValue(SalesforceClientFactory.OAuthStartExpiresAtKey, out var expiresAt) ||
            !long.TryParse(expiresAt, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAtUnixSeconds))
        {
            resolution = new(ExternalAuthorizationResolutionState.Failed, "authorization-start-invalid");
            return true;
        }

        resolution = expiresAtUnixSeconds >= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            ? new(ExternalAuthorizationResolutionState.Waiting)
            : new(ExternalAuthorizationResolutionState.Failed, "authorization-start-expired");
        return true;
    }

    private static bool IsCurrentPersistedOAuthStartToken(
        IReadOnlyDictionary<string, string> pending,
        string startToken)
    {
        if (!pending.TryGetValue(SalesforceClientFactory.OAuthPhaseKey, out var phase) ||
            (!string.Equals(phase, SalesforceClientFactory.OAuthPhaseLocalStart, StringComparison.Ordinal) &&
             !string.Equals(phase, SalesforceClientFactory.OAuthPhaseChallengeIssued, StringComparison.Ordinal)) ||
            !pending.TryGetValue(SalesforceClientFactory.OAuthStartTokenFingerprintKey, out var expectedFingerprint) ||
            !pending.TryGetValue(SalesforceClientFactory.OAuthStartExpiresAtKey, out var expiresAt) ||
            !long.TryParse(expiresAt, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAtUnixSeconds) ||
            expiresAtUnixSeconds < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return false;
        }

        return SalesforceClientFactory.SameAuthorizationAttempt(
            expectedFingerprint,
            SalesforceClientFactory.AuthorizationAttemptFingerprint(startToken));
    }

    private bool TryGetReusableLocalStartToken(
        IReadOnlyDictionary<string, string> pending,
        NeuronId owner,
        out string startToken)
    {
        startToken = string.Empty;
        if (!pending.TryGetValue(SalesforceClientFactory.OAuthStartTokenKey, out var candidate) ||
            string.IsNullOrWhiteSpace(candidate) ||
            !IsCurrentPersistedOAuthStartToken(pending, candidate) ||
            !oauthStateProtector.TryUnprotect(candidate, out var protectedOwner) ||
            !string.Equals(protectedOwner.Value, owner.Value, StringComparison.Ordinal))
            return false;

        startToken = candidate;
        return true;
    }

    private bool IsCurrentLegacyOAuthStartToken(string startToken)
    {
        var expectedHash = continuationState.State.OAuthStartTokenHash;
        return expectedHash is { Length: 32 } &&
               continuationState.State.OAuthStartExpiresAtUnixSeconds >= DateTimeOffset.UtcNow.ToUnixTimeSeconds() &&
               CryptographicOperations.FixedTimeEquals(expectedHash, HashOAuthStartToken(startToken));
    }

    private async Task ConsumeLegacyOAuthStartTokenAsync()
    {
        var previousState = continuationState.State;
        continuationState.State = new SalesforceReadNeuronState
        {
            SerializedContinuations = previousState.SerializedContinuations
        };
        try
        {
            await continuationState.WriteStateAsync(CancellationToken.None);
        }
        catch
        {
            continuationState.State = previousState;
            throw;
        }
    }

    private Task TerminalizePersistedOAuthStartAsync(
        string userScope,
        IReadOnlyDictionary<string, string> pending,
        string result)
    {
        var terminal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SalesforceClientFactory.OAuthPhaseKey] = SalesforceClientFactory.OAuthPhaseFailed,
            [SalesforceClientFactory.OAuthResultKey] = result,
            [SalesforceClientFactory.OAuthPendingExpiresAtKey] = DateTimeOffset.UtcNow
                .Add(SalesforceClientFactory.OAuthPendingLifetime)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture)
        };
        if (pending.TryGetValue(SalesforceClientFactory.OAuthFlowIdKey, out var flowId) &&
            SalesforceClientFactory.IsAuthorizationFlowId(flowId))
            terminal[SalesforceClientFactory.OAuthFlowIdKey] = flowId;
        return store.SetAsync(
            userScope,
            SalesforceClientFactory.OAuthPendingPackName,
            terminal,
            CancellationToken.None);
    }

    private static byte[] HashOAuthStartToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static bool IsPermissionFailure(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message.Contains("insufficient_access", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("forbidden", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthorizationFailure(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("reconnect", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("invalid session", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("revoked", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
    }
}

[GenerateSerializer, Alias("digitalbrain.salesforce.read-neuron-state")]
public sealed class SalesforceReadNeuronState
{
    [Id(0)] public byte[] SerializedContinuations { get; set; } = [];
    [Id(1)] public byte[] OAuthStartTokenHash { get; set; } = [];
    [Id(2)] public long OAuthStartExpiresAtUnixSeconds { get; set; }
}

internal sealed record SalesforceStoredContinuation(
    string Token,
    string NextRecordsUrl,
    string OrganizationId,
    string SalesforceUserId,
    string EntityLabel,
    string RecordIdField,
    Dictionary<string, string> FieldLabels)
{
    internal static SalesforceStoredContinuation From(string token, SalesforceContinuation continuation) =>
        new(
            token,
            continuation.NextRecordsUrl,
            continuation.Scope.OrganizationId,
            continuation.Scope.SalesforceUserId,
            continuation.EntityLabel,
            continuation.RecordIdField,
            continuation.FieldLabels.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    internal SalesforceContinuation ToProviderContinuation() =>
        new(
            NextRecordsUrl,
            new SalesforceProviderScope(OrganizationId, SalesforceUserId),
            EntityLabel,
            RecordIdField,
            new Dictionary<string, string>(FieldLabels, StringComparer.Ordinal));
}

internal static class SalesforceContinuationStateCodec
{
    private const int CurrentVersion = 1;
    private const int MaximumContinuations = 32;
    private const int MaximumPayloadBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static byte[] Encode(IReadOnlyList<SalesforceStoredContinuation> continuations)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new SalesforceContinuationStateEnvelope(CurrentVersion, continuations),
            Json);
        if (payload.Length > MaximumPayloadBytes)
            throw new SalesforceReadException(
                SalesforceReadFailure.LimitReached,
                "The Salesforce continuation state limit was reached. Start a new bounded read.");
        return payload;
    }

    internal static bool TryDecode(
        byte[]? payload,
        out IReadOnlyList<SalesforceStoredContinuation> continuations)
    {
        continuations = [];
        if (payload is null || payload.Length == 0)
            return true;
        if (payload.Length > MaximumPayloadBytes)
            return false;
        try
        {
            var envelope = JsonSerializer.Deserialize<SalesforceContinuationStateEnvelope>(payload, Json);
            if (envelope is null || envelope.Version != CurrentVersion ||
                envelope.Continuations is null || envelope.Continuations.Count > MaximumContinuations)
                return false;
            if (envelope.Continuations.Any(static continuation => continuation is null || !IsValid(continuation)))
                return false;
            continuations = envelope.Continuations;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValid(SalesforceStoredContinuation continuation) =>
        Guid.TryParseExact(continuation.Token, "N", out _) &&
        continuation.NextRecordsUrl is { Length: > 0 and <= 8192 } &&
        continuation.OrganizationId is { Length: > 0 and <= 256 } &&
        continuation.SalesforceUserId is { Length: > 0 and <= 256 } &&
        continuation.EntityLabel is { Length: > 0 and <= 256 } &&
        continuation.RecordIdField is { Length: > 0 and <= 256 } &&
        continuation.FieldLabels is { Count: <= 64 } &&
        continuation.FieldLabels.All(static item =>
            item.Key is { Length: > 0 and <= 256 } && item.Value is { Length: > 0 and <= 256 });

    private sealed record SalesforceContinuationStateEnvelope(
        int Version,
        IReadOnlyList<SalesforceStoredContinuation> Continuations);
}
