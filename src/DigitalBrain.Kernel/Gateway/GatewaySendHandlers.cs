using DigitalBrain.Core;
using DigitalBrain.Core.Config;

using DigitalBrain.Google;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Salesforce;
using DigitalBrain.Telegram;
using Grpc.Core;

namespace DigitalBrain.Kernel.Gateway;

using DigitalBrain.Pack.Contracts;
using DigitalBrain.Ui.Contracts;

internal sealed record GatewaySendContext(
    IGrainFactory Grains,
    IConfiguration Configuration,
    IHostEnvironment Environment,
    ILogger Logger,
    IPackConfigStore? PackConfigStore,
    Func<string?, Task<UserSessionState?>> ResolveSessionByClientIdAsync);

internal interface IGatewaySendHandler
{
    Task<bool> TryHandleAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context);
}

internal static class GatewaySendHandlers
{
    public static IReadOnlyList<IGatewaySendHandler> Default { get; } =
    [
        new GatewayMarketplaceSendHandler(),
        new GatewayAuthSessionSendHandler(),
        new GatewayConfigSendHandler(),
        new GatewayInoSendHandler(),
        new GatewayExperienceStepSendHandler(),
        new GatewayGenericSignalSendHandler()
    ];

    internal static bool TypeMatches(SynapseEnvelope request, string typeName) =>
        request.TypeName == typeName || request.TypeName.Contains(typeName, StringComparison.OrdinalIgnoreCase);

    internal static string PayloadString(SynapseEnvelope request) =>
        System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
}

internal sealed class GatewayMarketplaceSendHandler : IGatewaySendHandler
{
    public async Task<bool> TryHandleAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        if (GatewaySendHandlers.TypeMatches(request, nameof(PublishToMarketplace)))
        {
            await PublishAsync(request, serverContext, context);
            return true;
        }

        if (GatewaySendHandlers.TypeMatches(request, nameof(InstallFromMarketplace)))
        {
            await InstallAsync(request, serverContext, context);
            return true;
        }

        return false;
    }

    private static async Task PublishAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        var market = context.Grains.GetGrain<IMarketplaceNeuron>("market-main");
        var payloadStr = GatewaySendHandlers.PayloadString(request);
        var p = GatewayPayload.CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
        string Field(string key, string fallback = "") => p.TryGetValue(key, out var v) ? v?.ToString() ?? fallback : fallback;
        var packName = Field("packName", Field("name", request.CorrelationId));
        var isPrivate = bool.TryParse(Field("isPrivate"), out var priv) && priv;
        var commissionRate = double.TryParse(Field("commissionRate"), System.Globalization.CultureInfo.InvariantCulture, out var cr) ? cr : 0.10;
        var price = decimal.TryParse(Field("price"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pr) ? pr : 0m;
        await market.FireAsync(new PublishToMarketplace(
            packName, Field("version"), Field("code"), Field("ownerId", "anonymous"),
            isPrivate, commissionRate, Field("description"),
            Field("authorPublicKeyBase64"), Field("signatureBase64"), price), serverContext.CancellationToken);
    }

    private static async Task InstallAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        var market = context.Grains.GetGrain<IMarketplaceNeuron>("market-main");
        var payloadStr = GatewaySendHandlers.PayloadString(request);
        var p = GatewayPayload.CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
        var packName = p.TryGetValue("packName", out var pn) ? pn?.ToString() ?? p.GetValueOrDefault("name")?.ToString() ?? "" : "";
        var ver = p.TryGetValue("version", out var v) ? v?.ToString() ?? "" : "";
        var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() : null;
        var installSession = await context.ResolveSessionByClientIdAsync(clientId);
        var buyer = installSession?.UserId.Value ?? "anonymous";
        if (string.IsNullOrWhiteSpace(packName)) packName = request.CorrelationId;
        await market.FireAsync(new InstallFromMarketplace(packName, ver, buyer, clientId), serverContext.CancellationToken);
    }
}

internal sealed class GatewayAuthSessionSendHandler : IGatewaySendHandler
{
    public async Task<bool> TryHandleAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        if (GatewaySendHandlers.TypeMatches(request, GoogleSignals.AuthRequested))
        {
            await HandleGoogleAuthRequestedAsync(request, serverContext, context);
            return true;
        }

        if (GatewaySendHandlers.TypeMatches(request, GoogleSignals.AuthCompleted))
        {
            await HandleGoogleAuthCompletedAsync(request, serverContext, context);
            return true;
        }

        if (GatewaySendHandlers.TypeMatches(request, SalesforceSignals.AuthRequested))
        {
            await HandleSalesforceAuthRequestedAsync(request, serverContext, context);
            return true;
        }

        if (GatewaySendHandlers.TypeMatches(request, nameof(LoginRequest)))
        {
            await HandleLoginAsync(request, serverContext, context);
            return true;
        }

        if (GatewaySendHandlers.TypeMatches(request, nameof(LogoutRequest)))
        {
            await HandleLogoutAsync(request, serverContext, context);
            return true;
        }

        return false;
    }

    private static async Task HandleGoogleAuthRequestedAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        var authProps = GatewayPayload.PayloadProps(request);
        var authClientId = authProps.TryGetValue("clientId", out var authCid) ? authCid?.ToString() : null;
        var authSession = await context.ResolveSessionByClientIdAsync(authClientId);
        if (authSession is null)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "A real login session is required to connect Google."));

        var auth = context.Grains.GetGrain<IGoogleAuthNeuron>(authSession.UserId.Value);
        var signal = new Signal(GoogleSignals.AuthRequested, authProps)
        {
            Receiver = new NeuronId(authSession.UserId.Value)
        };
        await auth.DeliverAsync(signal, serverContext.CancellationToken);
    }

    private static async Task HandleGoogleAuthCompletedAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        var key = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? "google-auth-completed"
            : request.CorrelationId;
        var authCompletedIngress = context.Grains.GetGrain<IIngressNeuron>(key);
        await authCompletedIngress.IngestAsync(GoogleSignals.AuthCompleted, GatewayPayload.PayloadProps(request), serverContext.CancellationToken);
    }

    private static async Task HandleSalesforceAuthRequestedAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        var authProps = GatewayPayload.PayloadProps(request);
        var authClientId = authProps.TryGetValue("clientId", out var authCid) ? authCid?.ToString() : null;
        var authSession = await context.ResolveSessionByClientIdAsync(authClientId);
        if (authSession is null)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "A real login session is required to connect Salesforce."));

        var auth = context.Grains.GetGrain<ISalesforceAuthNeuron>(authSession.UserId.Value);
        var signal = new Signal(SalesforceSignals.AuthRequested, authProps)
        {
            Receiver = new NeuronId(authSession.UserId.Value)
        };
        await auth.DeliverAsync(signal, serverContext.CancellationToken);
    }

    private static async Task HandleLoginAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        var session = context.Grains.GetGrain<IUserSessionNeuron>("session-main");
        var payloadStr = GatewaySendHandlers.PayloadString(request);
        var p = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr) ?? new();
        var username = p.TryGetValue("username", out var u) ? u?.ToString() ?? "" : "";
        var password = p.TryGetValue("password", out var pw) ? pw?.ToString() ?? "" : "";
        var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() ?? "grpc" : "grpc";
        await session.FireAsync(new LoginRequest(username, password, clientId), serverContext.CancellationToken);
    }

    private static async Task HandleLogoutAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        var session = context.Grains.GetGrain<IUserSessionNeuron>("session-main");
        var payloadStr = GatewaySendHandlers.PayloadString(request);
        var p = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr) ?? new();
        var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() ?? "grpc" : "grpc";
        var logoutSession = await context.ResolveSessionByClientIdAsync(clientId);
        await session.FireAsync(new LogoutRequest(logoutSession?.SessionId ?? "", clientId), serverContext.CancellationToken);
    }
}

internal sealed class GatewayConfigSendHandler : IGatewaySendHandler
{
    public async Task<bool> TryHandleAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        if (request.TypeName != nameof(ConfigurationProvided))
        {
            return false;
        }

        if (context.PackConfigStore is null)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Pack config store is not configured."));

        var payloadStr = GatewaySendHandlers.PayloadString(request);
        var p = GatewayPayload.CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
        string? Field(string key) => p.TryGetValue(key, out var v) ? v?.ToString() : null;

        var pack = Field("pack") ?? Field("packName") ?? request.CorrelationId;
        var scope = Field("scope") ?? PackConfigScopes.App;

        var configSession = await context.ResolveSessionByClientIdAsync(Field("clientId"));
        var callerOwnScope = configSession is not null ? PackConfigScopes.ForUser(configSession.UserId) : null;
        if (scope != PackConfigScopes.App && scope != callerOwnScope)
            throw new RpcException(new Status(StatusCode.PermissionDenied, $"Scope '{scope}' is not permitted for this caller."));

        var controlKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pack", "packName", "scope", "clientId", "buyerId", "userId", "workspaceId", "synapseType", "eventName"
        };
        var values = p
            .Where(kv => !controlKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty);

        await context.PackConfigStore.SetAsync(scope, pack, values, serverContext.CancellationToken);
        context.Logger.LogInformation("Stored configuration for pack {Pack} ({FieldCount} fields).", pack, values.Count);

        var notifyKey = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? $"pack-configured-{scope}-{pack}"
            : request.CorrelationId;
        var notifyIngress = context.Grains.GetGrain<IIngressNeuron>(notifyKey);
        await notifyIngress.IngestAsync("PackConfigured", new Dictionary<string, object?>
        {
            ["pack"] = pack,
            ["scope"] = scope
        }, serverContext.CancellationToken);
        return true;
    }
}

internal sealed class GatewayInoSendHandler : IGatewaySendHandler
{
    public async Task<bool> TryHandleAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        if (!GatewaySendHandlers.TypeMatches(request, nameof(InoRequest)))
        {
            return false;
        }

        var payloadStr = GatewaySendHandlers.PayloadString(request);
        var p = GatewayPayload.CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
        var prompt = p.TryGetValue("prompt", out var pr) ? pr?.ToString() ?? "" : "";
        var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() : null;
        var workspaceId = p.TryGetValue("workspaceId", out var wid) ? wid?.ToString() : null;

        var ino = context.Grains.GetGrain<IInoNeuron>("ino-main");
        await ino.FireAsync(new InoRequest(prompt, clientId, workspaceId), serverContext.CancellationToken);
        return true;
    }
}

internal sealed class GatewayExperienceStepSendHandler : IGatewaySendHandler
{
    public async Task<bool> TryHandleAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        if (!GatewaySendHandlers.TypeMatches(request, nameof(ExperienceStep)))
        {
            return false;
        }

        var payloadStr = GatewaySendHandlers.PayloadString(request);
        var p = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(payloadStr) ?? new();
        var pack = p.GetValueOrDefault("pack", "");
        var experienceId = p.GetValueOrDefault("experienceId", "");
        var eventName = p.GetValueOrDefault("eventName", "start");
        var args = p.Where(kv => kv.Key is not ("pack" or "experienceId" or "eventName" or "synapseType"))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
        var generated = context.Grains.GetGrain<IGeneratedNeuron>("generated-" + pack.ToLowerInvariant());
        await generated.FireAsync(new ExperienceStep(pack, experienceId, eventName, args), serverContext.CancellationToken);
        return true;
    }
}

internal sealed class GatewayGenericSignalSendHandler : IGatewaySendHandler
{
    public async Task<bool> TryHandleAsync(SynapseEnvelope request, ServerCallContext serverContext, GatewaySendContext context)
    {
        if (string.IsNullOrWhiteSpace(request.TypeName))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Empty synapse type"));

        GatewayInternalAuth.Enforce(context.Configuration, context.Environment, context.Logger, serverContext, nameof(GatewayService.Send));

        var payloadJson = GatewaySendHandlers.PayloadString(request);
        var rawProps = string.IsNullOrWhiteSpace(payloadJson)
            ? new Dictionary<string, object?>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson) ?? new();
        var signalProps = GatewayPayload.NormalizeJsonProps(rawProps);

        if (string.Equals(request.TypeName, TelegramSignals.MessageReceived, StringComparison.Ordinal)
            && signalProps.TryGetValue("chatId", out var chatIdValue) && chatIdValue is not null)
        {
            var chatKey = "tg-chat-" + Convert.ToInt64(chatIdValue);
            var chat = context.Grains.GetGrain<ITelegramChatNeuron>(chatKey);
            await chat.DeliverAsync(new Signal(request.TypeName, signalProps), serverContext.CancellationToken);
            return true;
        }

        var ingress = context.Grains.GetGrain<IIngressNeuron>(request.CorrelationId);
        await ingress.IngestAsync(request.TypeName, signalProps, serverContext.CancellationToken);
        return true;
    }
}
