using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.V2;
using DigitalBrain.Ui.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.Google;

[GrainType("digitalbrain.google.gmail.v1")]
public class GmailNeuron(ILogger<GmailNeuron> logger, NeuronJournals journals, IGmailApiClientFactory? gmailApiClientFactory = null)
    : Neuron(logger, journals), IGmailNeuron, IV2GmailReadToolGrain
{
    private const int DefaultMessageLimit = 10;

    public async Task<bool> EnsureConnectedAsync(string? clientId, CancellationToken cancellationToken = default) =>
        await TryGetConnectedScopeAsync(clientId, cancellationToken) is not null;

    public async Task HandleAsync(CapabilityInvocation invocation, CancellationToken cancellationToken = default)
    {
        var scope = await TryGetConnectedScopeAsync(invocation.ClientId, cancellationToken);
        if (scope is null)
        {
            return;
        }

        try
        {
            if (gmailApiClientFactory is null)
            {
                await DeliverTextSurfaceAsync(
                    "Gmail is not configured in this host.",
                    invocation.ClientId,
                    invocation.WorkspaceId,
                    cancellationToken);
                return;
            }

            var client = await gmailApiClientFactory.CreateAsync(scope.Value, cancellationToken);
            var messages = await client.ListMessagesAsync(invocation.Prompt, DefaultMessageLimit, cancellationToken);
            await DeliverTextSurfaceAsync(
                messages.Length == 0
                    ? "Gmail returned no messages."
                    : "Gmail messages:\n" + string.Join("\n", messages.Take(DefaultMessageLimit)),
                invocation.ClientId,
                invocation.WorkspaceId,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (IsAuthOrConfigFailure(ex))
            {
                await RequestAuthAsync(scope.Value.UserId, invocation.ClientId, cancellationToken);
                return;
            }

            Logger.LogWarning(ex, "Gmail capability invocation failed.");
            await DeliverTextSurfaceAsync(
                "Gmail failed: " + ex.GetBaseException().Message,
                invocation.ClientId,
                invocation.WorkspaceId,
                cancellationToken);
        }
    }

    public async Task<string[]> ListMessagesAsync(string query, int maxResults = 20, CancellationToken ct = default)
    {
        var factory = gmailApiClientFactory ?? throw new InvalidOperationException("Gmail API client factory is not configured.");
        var client = await factory.CreateAsync(Self.AsScope(), ct);
        return await client.ListMessagesAsync(query, maxResults, ct);
    }

    public async Task<string[]> ListMessagesForClientAsync(string? clientId, string query, int maxResults = 20, CancellationToken ct = default)
    {
        var factory = gmailApiClientFactory ?? throw new InvalidOperationException("Gmail API client factory is not configured.");
        var scope = await ResolveConnectedScopeOrThrowAsync(clientId, ct);
        var client = await factory.CreateAsync(scope, ct);
        return await client.ListMessagesAsync(query, maxResults, ct);
    }

    public async Task<string> ReadMessageAsync(string messageId, CancellationToken ct = default)
    {
        var factory = gmailApiClientFactory ?? throw new InvalidOperationException("Gmail API client factory is not configured.");
        var client = await factory.CreateAsync(Self.AsScope(), ct);
        return await client.ReadMessageAsync(messageId, ct);
    }

    public async Task<string> ReadMessageForClientAsync(string? clientId, string messageId, CancellationToken ct = default)
    {
        var factory = gmailApiClientFactory ?? throw new InvalidOperationException("Gmail API client factory is not configured.");
        var scope = await ResolveConnectedScopeOrThrowAsync(clientId, ct);
        var client = await factory.CreateAsync(scope, ct);
        return await client.ReadMessageAsync(messageId, ct);
    }

    public async Task SendMessageAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var factory = gmailApiClientFactory ?? throw new InvalidOperationException("Gmail API client factory is not configured.");
        var client = await factory.CreateAsync(Self.AsScope(), ct);
        await client.SendMessageAsync(to, subject, body, ct);
    }

    public async Task<V2GmailReadResult> ReadLatestIncomingAsync(CancellationToken cancellationToken = default)
    {
        var scope = Self.AsScope();
        if (!await HasCredentialAsync(scope, cancellationToken))
            return await BuildConnectionResultAsync(scope.UserId, cancellationToken);

        try
        {
            var factory = gmailApiClientFactory ?? throw new InvalidOperationException("Gmail is not configured in this host.");
            var client = await factory.CreateAsync(scope, cancellationToken);
            var messages = await client.ListMessagesAsync("in:inbox", 1, cancellationToken);
            if (messages.Length == 0)
                return new V2GmailReadResult(V2GmailReadStatus.Success, "No incoming Gmail messages were found.");

            var content = await client.ReadMessageAsync(messages[0], cancellationToken);
            return new V2GmailReadResult(
                V2GmailReadStatus.Success,
                string.IsNullOrWhiteSpace(content) ? "The latest incoming Gmail message has no preview text." : content.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsAuthOrConfigFailure(ex))
        {
            return await BuildConnectionResultAsync(scope.UserId, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Principal-scoped Gmail read failed.");
            return new V2GmailReadResult(
                V2GmailReadStatus.Unavailable,
                SafeReason: "I couldn’t read Gmail right now. Please try again later.");
        }
    }

    private async Task<V2GmailReadResult> BuildConnectionResultAsync(
        UserId userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var connector = ServiceProvider.GetRequiredKeyedService<IConnector>("google");
            var challenge = await connector.BeginAuthAsync(new NeuronId(userId.Value), cancellationToken: cancellationToken);
            if (challenge.IsForm || !IsAllowedGoogleAuthorizationUrl(challenge.UrlOrForm))
            {
                return new V2GmailReadResult(
                    V2GmailReadStatus.Unavailable,
                    SafeReason: "Gmail needs to be configured before you can connect an account.");
            }

            return new V2GmailReadResult(
                V2GmailReadStatus.NeedsAuth,
                SafeReason: "Connect your Google account to let INO read your Gmail.",
                ConnectionUrl: challenge.UrlOrForm);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Principal-scoped Google connection challenge failed.");
            return new V2GmailReadResult(
                V2GmailReadStatus.Unavailable,
                SafeReason: "Gmail connection is unavailable right now. Please try again later.");
        }
    }

    private static bool IsAllowedGoogleAuthorizationUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase);

    private async Task<NeuronScope?> TryGetConnectedScopeAsync(string? clientId, CancellationToken cancellationToken)
    {
        var scope = await ResolveUserScopeOrPromptLoginAsync(clientId, cancellationToken);
        if (scope is null)
        {
            return null;
        }

        if (!await HasCredentialAsync(scope.Value, cancellationToken))
        {
            await RequestAuthAsync(scope.Value.UserId, clientId, cancellationToken);
            return null;
        }

        return scope;
    }

    private async Task<NeuronScope> ResolveConnectedScopeOrThrowAsync(string? clientId, CancellationToken cancellationToken)
    {
        var scope = await TryGetConnectedScopeAsync(clientId, cancellationToken);
        return scope ?? throw new InvalidOperationException("Google account is not connected for this session.");
    }

    private async Task<NeuronScope?> ResolveUserScopeOrPromptLoginAsync(string? clientId, CancellationToken cancellationToken)
    {
        var session = await ResolveSessionAsync(clientId, cancellationToken);
        if (session is not null)
        {
            return new NeuronScope(session.UserId, ThreadId: null);
        }

        var sessionNeuron = GrainFactory.GetGrain<IUserSessionNeuron>(IUserSessionNeuron.SingletonKey);
        var surface = await sessionNeuron.BuildLoginSurfaceAsync(clientId);
        await DeliverSurfaceAsync(surface, cancellationToken);
        return null;
    }

    private async Task<UserSessionState?> ResolveSessionAsync(string? clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var session = GrainFactory.GetGrain<IUserSessionNeuron>(IUserSessionNeuron.SingletonKey);
        return await session.GetSessionByClientIdAsync(clientId);
    }

    private async Task<bool> HasCredentialAsync(NeuronScope scope, CancellationToken cancellationToken)
    {
        var store = ServiceProvider.GetService<IPackConfigStore>();
        if (store is null)
        {
            return false;
        }

        var values = await GoogleClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        return GoogleClientFactory.HasUsableCredential(values);
    }

    private async Task RequestAuthAsync(UserId userId, string? clientId, CancellationToken cancellationToken)
    {
        var props = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            props["clientId"] = clientId;
        }

        var auth = GrainFactory.GetGrain<INeuron>(
            GrainId.Create("digitalbrain.google.auth.v1", userId.Value));
        await auth.DeliverAsync(StampCurrent(new Signal(GoogleSignals.AuthRequested, props)), cancellationToken);
    }

    private async Task DeliverTextSurfaceAsync(
        string text,
        string? clientId,
        string? workspaceId,
        CancellationToken cancellationToken)
    {
        var props = new Dictionary<string, object?>
        {
            ["tree"] = new UiWidgetTree(UiKitVocabulary.Text, new Dictionary<string, object?> { ["text"] = text }),
            [UiSurfaceKeys.Title] = "Gmail",
            [UiSurfaceKeys.Emitter] = Self.Value,
            ["workspaceId"] = WorkspaceIds.Effective(workspaceId)
        };
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            props["clientId"] = clientId;
        }

        await DeliverSurfaceAsync(new UiSurface(UiSurface.WidgetTreeKind, props), cancellationToken);
    }

    private async Task DeliverSurfaceAsync(UiSurface surface, CancellationToken cancellationToken)
    {
        await FireAsync(surface, cancellationToken);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>(IFlutterUiNeuron.SingletonKey);
        await flutter.DeliverAsync(StampCurrent(surface), cancellationToken);
    }

    private static bool IsAuthOrConfigFailure(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("config", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("missing", StringComparison.OrdinalIgnoreCase);
    }
}
