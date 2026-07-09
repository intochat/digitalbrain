using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using DigitalBrain.Ui.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.Salesforce;

[GrainType("digitalbrain.salesforce.crm.v1")]
public class SalesforceCrmNeuron(
    ILogger<SalesforceCrmNeuron> logger,
    NeuronJournals journals,
    ISalesforceApiClientFactory? apiClientFactory = null)
    : Neuron(logger, journals), ISalesforceCrmNeuron
{
    private const int DefaultAccountLimit = 20;

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
            if (apiClientFactory is null)
            {
                await DeliverTextSurfaceAsync(
                    "Salesforce is not configured in this host.",
                    invocation.ClientId,
                    invocation.WorkspaceId,
                    cancellationToken);
                return;
            }

            var client = await apiClientFactory.CreateAsync(scope.Value, cancellationToken);
            var records = await client.ListAccountsAsync(DefaultAccountLimit, cancellationToken);
            await DeliverTextSurfaceAsync(
                records.Length == 0
                    ? "Salesforce returned no accounts."
                    : "Salesforce accounts:\n" + string.Join("\n", records.Take(DefaultAccountLimit)),
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

            Logger.LogWarning(ex, "Salesforce capability invocation failed.");
            await DeliverTextSurfaceAsync(
                "Salesforce failed: " + ex.GetBaseException().Message,
                invocation.ClientId,
                invocation.WorkspaceId,
                cancellationToken);
        }
    }

    public async Task<string[]> QueryAsync(string soql, CancellationToken ct = default)
    {
        var factory = apiClientFactory ?? throw new InvalidOperationException("Salesforce API client factory is not configured.");
        var client = await factory.CreateAsync(Self.AsScope(), ct);
        return await client.QueryAsync(soql, ct);
    }

    public async Task<string[]> QueryForClientAsync(string? clientId, string soql, CancellationToken ct = default)
    {
        var factory = apiClientFactory ?? throw new InvalidOperationException("Salesforce API client factory is not configured.");
        var scope = await ResolveConnectedScopeOrThrowAsync(clientId, ct);
        var client = await factory.CreateAsync(scope, ct);
        return await client.QueryAsync(soql, ct);
    }

    public async Task<string[]> ListAccountsAsync(int maxResults = 20, CancellationToken ct = default)
    {
        var factory = apiClientFactory ?? throw new InvalidOperationException("Salesforce API client factory is not configured.");
        var client = await factory.CreateAsync(Self.AsScope(), ct);
        return await client.ListAccountsAsync(maxResults, ct);
    }

    public async Task<string[]> ListAccountsForClientAsync(string? clientId, int maxResults = 20, CancellationToken ct = default)
    {
        var factory = apiClientFactory ?? throw new InvalidOperationException("Salesforce API client factory is not configured.");
        var scope = await ResolveConnectedScopeOrThrowAsync(clientId, ct);
        var client = await factory.CreateAsync(scope, ct);
        return await client.ListAccountsAsync(maxResults, ct);
    }

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
        return scope ?? throw new InvalidOperationException("Salesforce account is not connected for this session.");
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

        var values = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        return SalesforceClientFactory.HasUsableCredential(values);
    }

    private async Task RequestAuthAsync(UserId userId, string? clientId, CancellationToken cancellationToken)
    {
        var props = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            props["clientId"] = clientId;
        }

        var auth = GrainFactory.GetGrain<INeuron>(
            GrainId.Create("digitalbrain.salesforce.auth.v1", userId.Value));
        await auth.DeliverAsync(StampCurrent(new Signal(SalesforceSignals.AuthRequested, props)), cancellationToken);
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
            [UiSurfaceKeys.Title] = "Salesforce CRM",
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
