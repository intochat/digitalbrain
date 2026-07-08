using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Ui.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.Google;

[GrainType("digitalbrain.google.gmail.v1")]
public class GmailNeuron(ILogger<GmailNeuron> logger, NeuronJournals journals, IGmailApiClientFactory? gmailApiClientFactory = null)
    : Neuron(logger, journals), IGmailNeuron
{
    private const int DefaultMessageLimit = 10;

    public async Task HandleAsync(CapabilityInvocation invocation, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveUserScopeOrPromptLoginAsync(invocation.ClientId, cancellationToken);
        if (scope is null)
        {
            return;
        }

        if (!await HasCredentialAsync(scope.Value, cancellationToken))
        {
            await RequestAuthAsync(scope.Value.UserId, invocation.ClientId, cancellationToken);
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

    public async Task<string> ReadMessageAsync(string messageId, CancellationToken ct = default)
    {
        var factory = gmailApiClientFactory ?? throw new InvalidOperationException("Gmail API client factory is not configured.");
        var client = await factory.CreateAsync(Self.AsScope(), ct);
        return await client.ReadMessageAsync(messageId, ct);
    }

    public async Task SendMessageAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var factory = gmailApiClientFactory ?? throw new InvalidOperationException("Gmail API client factory is not configured.");
        var client = await factory.CreateAsync(Self.AsScope(), ct);
        await client.SendMessageAsync(to, subject, body, ct);
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
