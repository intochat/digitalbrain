using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Core.Ui;
using DigitalBrain.Demo.Runtime;
using DigitalBrain.Google;
using DigitalBrain.Kernel.Auth;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Salesforce;
using DigitalBrain.Telegram.Channel;
using Grpc.Core;

namespace DigitalBrain.Kernel.Gateway;

public sealed class GatewayService(
    IGrainFactory grains,
    IConfiguration configuration,
    HomeFeedBus homeFeedBus,
    SignalEgressBus signalEgressBus,
    IHostEnvironment environment,
    ILogger<GatewayService> logger,
    IPackConfigStore? packConfigStore = null) : DigitalBrainGateway.DigitalBrainGatewayBase
{
    public override async Task<SynapseEnvelope> Send(SynapseEnvelope request, ServerCallContext context)
    {
        try
        {
            if (request.TypeName == SurfaceDemoRuntime.RequestType)
            {
                await InstallAndRunSurfaceDemoAsync(request.CorrelationId);
                return request;
            }

            // Publish a pack to the marketplace. Payload carries the pack fields (and optional signature).
            // Without this, "PublishToMarketplace" fell through to the generic fallback and the pack code was
            // dropped, so nothing could later be installed/embodied.
            if (request.TypeName == nameof(PublishToMarketplace) || request.TypeName.Contains("PublishToMarketplace", StringComparison.OrdinalIgnoreCase))
            {
                var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
                string Field(string key, string fallback = "") => p.TryGetValue(key, out var v) ? v?.ToString() ?? fallback : fallback;
                var packName = Field("packName", Field("name", request.CorrelationId));
                var isPrivate = bool.TryParse(Field("isPrivate"), out var priv) && priv;
                var commissionRate = double.TryParse(Field("commissionRate"), System.Globalization.CultureInfo.InvariantCulture, out var cr) ? cr : 0.10;
                var price = decimal.TryParse(Field("price"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pr) ? pr : 0m;
                await market.FireAsync(new PublishToMarketplace(
                    packName, Field("version"), Field("code"), Field("ownerId", "anonymous"),
                    isPrivate, commissionRate, Field("description"),
                    Field("authorPublicKeyBase64"), Field("signatureBase64"), price));
                return request;
            }

            // Generic surface action dispatch (from UI kit RFW events / descriptors).
            // Supports install from MarketplaceList + run experiences from InstalledBundles via neurons/synapses.
            if (request.TypeName == nameof(InstallFromMarketplace) || request.TypeName.Contains("InstallFromMarketplace", StringComparison.OrdinalIgnoreCase))
            {
                var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
                // payload json carries props (packName/version from surface action); buyerId is server-resolved below
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
                var packName = p.TryGetValue("packName", out var pn) ? pn?.ToString() ?? p.GetValueOrDefault("name")?.ToString() ?? "" : "";
                var ver = p.TryGetValue("version", out var v) ? v?.ToString() ?? "" : "";
                var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() : null;
                var installSession = await ResolveSessionByClientIdAsync(clientId);
                var buyer = installSession?.UserId.Value ?? "anonymous";
                if (string.IsNullOrWhiteSpace(packName)) packName = request.CorrelationId; // fallback
                await market.FireAsync(new InstallFromMarketplace(packName, ver, buyer, clientId));
                return request;
            }

            if (request.TypeName == GoogleSignals.AuthRequested || request.TypeName.Contains(GoogleSignals.AuthRequested, StringComparison.OrdinalIgnoreCase))
            {
                var auth = grains.GetGrain<IGoogleAuthNeuron>("google-auth-main");
                var signal = new Signal(GoogleSignals.AuthRequested, PayloadProps(request))
                {
                    Receiver = new NeuronId("google-auth-main")
                };
                await auth.DeliverAsync(signal);
                return request;
            }

            if (request.TypeName == GoogleSignals.AuthCompleted || request.TypeName.Contains(GoogleSignals.AuthCompleted, StringComparison.OrdinalIgnoreCase))
            {
                var key = string.IsNullOrWhiteSpace(request.CorrelationId)
                    ? "google-auth-completed"
                    : request.CorrelationId;
                var authCompletedIngress = grains.GetGrain<IIngressNeuron>(key);
                await authCompletedIngress.IngestAsync(GoogleSignals.AuthCompleted, PayloadProps(request));
                return request;
            }

            if (request.TypeName == SalesforceSignals.AuthRequested || request.TypeName.Contains(SalesforceSignals.AuthRequested, StringComparison.OrdinalIgnoreCase))
            {
                var authProps = PayloadProps(request);
                var authSessionId = authProps.TryGetValue("sessionId", out var authSid) ? authSid?.ToString() : null;
                var authSession = await ResolveSessionByClientIdAsync(authSessionId);
                // TEMPORARY: the Flutter client has no channel yet to forward its real login session here
                // (see docs/superpowers/plans/2026-07-04-multiuser-s2-s3-identity-and-salesforce-per-user.md,
                // "Known Limitations" / live-bug follow-up) — fall back to a consistent "anonymous" identity
                // rather than hard-rejecting, restoring today's single-user functionality until the client is
                // updated to capture and forward a real session.
                var salesforceUserId = authSession?.UserId.Value ?? UserId.Anonymous.Value;
                var auth = grains.GetGrain<ISalesforceAuthNeuron>(salesforceUserId);
                var signal = new Signal(SalesforceSignals.AuthRequested, authProps)
                {
                    Receiver = new NeuronId(salesforceUserId)
                };
                await auth.DeliverAsync(signal);
                return request;
            }

            // A submitted config form round-trips here. Persist the field values for the pack via the encrypted
            // config store. The values may include secrets, so they are NEVER logged.
            if (request.TypeName == nameof(ConfigurationProvided))
            {
                if (packConfigStore is null)
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, "Pack config store is not configured."));

                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
                string? Field(string key) => p.TryGetValue(key, out var v) ? v?.ToString() : null;

                var pack = Field("pack") ?? Field("packName") ?? request.CorrelationId;
                var scope = Field("scope") ?? PackConfigScopes.App;

                // The scope must be either the shared app-level slot every reader (responder pack, LlmResponderNeuron,
                // Telegram transport) actually pulls from, or the caller's OWN resolved per-user slot — never an
                // arbitrary/other-user scope, per P6b.
                var configSession = await ResolveSessionByClientIdAsync(Field("clientId"));
                var callerOwnScope = configSession is not null ? PackConfigScopes.ForUser(configSession.UserId) : null;
                if (scope != PackConfigScopes.App && scope != callerOwnScope)
                    throw new RpcException(new Status(StatusCode.PermissionDenied, $"Scope '{scope}' is not permitted for this caller."));

                var controlKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "pack", "packName", "scope", "clientId", "buyerId", "userId", "synapseType", "eventName"
                };
                var values = p
                    .Where(kv => !controlKeys.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty);

                await packConfigStore.SetAsync(scope, pack, values);
                logger.LogInformation("Stored configuration for pack {Pack} ({FieldCount} fields).", pack, values.Count);

                // Non-secret notification only: subscribers learn config changed and re-PULL the values
                // point-to-point via GetPackConfig. The stored values (which may be secrets) are NOT broadcast.
                var notifyKey = string.IsNullOrWhiteSpace(request.CorrelationId)
                    ? $"pack-configured-{scope}-{pack}"
                    : request.CorrelationId;
                var notifyIngress = grains.GetGrain<IIngressNeuron>(notifyKey);
                await notifyIngress.IngestAsync("PackConfigured", new Dictionary<string, object?>
                {
                    ["pack"] = pack,
                    ["scope"] = scope
                });
                return request;
            }

            if (request.TypeName == nameof(InoRequest) || request.TypeName.Contains("InoRequest", StringComparison.OrdinalIgnoreCase))
            {
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
                var prompt = p.TryGetValue("prompt", out var pr) ? pr?.ToString() ?? "" : "";
                var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() : null;

                var ino = grains.GetGrain<IInoNeuron>("ino-main");
                await ino.FireAsync(new InoRequest(prompt, clientId));
                return request;
            }

            if (request.TypeName == nameof(LoginRequest) || request.TypeName.Contains("LoginRequest", StringComparison.OrdinalIgnoreCase))
            {
                var session = grains.GetGrain<IUserSessionNeuron>("session-main");
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr) ?? new();
                var username = p.TryGetValue("username", out var u) ? u?.ToString() ?? "" : "";
                var password = p.TryGetValue("password", out var pw) ? pw?.ToString() ?? "" : "";
                var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() ?? "grpc" : "grpc";
                await session.FireAsync(new LoginRequest(username, password, clientId));
                return request;
            }

            if (request.TypeName == nameof(LogoutRequest) || request.TypeName.Contains("LogoutRequest", StringComparison.OrdinalIgnoreCase))
            {
                var session = grains.GetGrain<IUserSessionNeuron>("session-main");
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr) ?? new();
                var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() ?? "grpc" : "grpc";
                var logoutSession = await ResolveSessionByClientIdAsync(clientId);
                await session.FireAsync(new LogoutRequest(logoutSession?.SessionId ?? "", clientId));
                return request;
            }

            if (request.TypeName == nameof(ExperienceStep) || request.TypeName.Contains("ExperienceStep", StringComparison.OrdinalIgnoreCase))
            {
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(payloadStr) ?? new();
                var pack = p.GetValueOrDefault("pack", "");
                var experienceId = p.GetValueOrDefault("experienceId", "");
                var eventName = p.GetValueOrDefault("eventName", "start");
                var args = p.Where(kv => kv.Key is not ("pack" or "experienceId" or "eventName" or "synapseType"))
                            .ToDictionary(kv => kv.Key, kv => kv.Value);
                var generated = grains.GetGrain<IGeneratedNeuron>("generated-" + pack.ToLowerInvariant());
                await generated.FireAsync(new ExperienceStep(pack, experienceId, eventName, args));
                return request;
            }

            // Generic fallback: any unknown type_name becomes a named Signal broadcast on the cluster timeline.
            // This path is INTERNAL-ONLY. Trusted in-cluster transports (the Telegram transport) present the shared
            // InternalServiceKey to fire arbitrary named synapses; an untrusted browser on the same external ingress
            // must not, or it could forge egress/reply signals (e.g. TelegramReplyRequested → arbitrary outbound
            // Telegram messages) or spoof inbound events. The known surface-action branches above stay open to the
            // Flutter client; only this arbitrary-type path is gated (same key + fail-closed rules as GetPackConfig).
            if (string.IsNullOrWhiteSpace(request.TypeName))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Empty synapse type"));

            EnforceInternalCaller(context);

            var payloadJson = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
            var rawProps = string.IsNullOrWhiteSpace(payloadJson)
                ? new Dictionary<string, object?>()
                : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson) ?? new();
            var signalProps = NormalizeJsonProps(rawProps);

            if (string.Equals(request.TypeName, TelegramSignals.MessageReceived, StringComparison.Ordinal)
                && signalProps.TryGetValue("chatId", out var chatIdValue) && chatIdValue is not null)
            {
                var chatKey = "tg-chat-" + Convert.ToInt64(chatIdValue);
                var chat = grains.GetGrain<ITelegramChatNeuron>(chatKey);
                await chat.DeliverAsync(new Signal(request.TypeName, signalProps));
                return request;
            }

            var ingress = grains.GetGrain<IIngressNeuron>(request.CorrelationId);
            await ingress.IngestAsync(request.TypeName, signalProps);
            return request;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Send failed for {TypeName}", request.TypeName);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    // Server-driven UI: stream RfwCards to the client as neurons broadcast them, until the client disconnects.
    public override async Task WatchHomeFeed(WatchHomeFeedRequest request, IServerStreamWriter<RfwCardEnvelope> responseStream, ServerCallContext context)
    {
        logger.LogInformation("WatchHomeFeed opened for {Peer}", context.Peer);
        var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? null : request.ClientId;

        // The first card a client sees is the login surface — pre-fill it with the dev credentials in
        // Development. clientId rides along on the form's submitAction payload (UiSurfaceRuntime.Login), so
        // the client's own submit button re-sends it with no further Flutter code needed for that leg.
        var initialLogin = DevAuth.Enabled(configuration, environment)
            ? UiSurfaceSamples.Login(clientId: clientId ?? "flutter", defaultUsername: DevAuth.Username, defaultPassword: DevAuth.Password)
            : UiSurfaceSamples.Login(clientId: clientId ?? "flutter");
        await WriteCardAsync(responseStream, UiSurfaceRfwBridge.FromUiSurface(initialLogin, "session-main"));
        logger.LogInformation("WatchHomeFeed sent initial login surface to {Peer}", context.Peer);

        await using var subscription = await homeFeedBus.SubscribeAsync(clientId);
        await foreach (var card in subscription.Reader.ReadAllAsync(context.CancellationToken))
        {
            await WriteCardAsync(responseStream, card);
        }
    }

    // Resolves a client-supplied clientId once; callers must use the result's fields downstream, never trust
    // a raw client-supplied userId/sessionId directly.
    private async Task<UserSessionState?> ResolveSessionByClientIdAsync(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return null;
        var session = grains.GetGrain<IUserSessionNeuron>("session-main");
        return await session.GetSessionByClientIdAsync(clientId);
    }

    // Egress for external transports: stream broadcast Signals whose Name is in the request filter (empty = all),
    // until the client disconnects. Each Signal becomes a SynapseEnvelope carrying its Props as UTF-8 JSON.
    public override async Task WatchSynapses(WatchSynapsesRequest request, IServerStreamWriter<SynapseEnvelope> responseStream, ServerCallContext context)
    {
        logger.LogInformation("WatchSynapses opened for {Peer} (filter: {Filter})", context.Peer, string.Join(",", request.TypeFilter));
        using var subscription = signalEgressBus.Subscribe(request.TypeFilter.ToArray());
        await foreach (var signal in subscription.Reader.ReadAllAsync(context.CancellationToken))
        {
            await responseStream.WriteAsync(new SynapseEnvelope
            {
                TypeName = signal.Name,
                CorrelationId = signal.CorrelationId ?? string.Empty,
                Payload = global::Google.Protobuf.ByteString.CopyFrom(
                    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(signal.Props))
            });
        }
    }

    // Point-to-point pull of a pack's decrypted config over the internal gRPC channel. Same trust level as the
    // startup env param — the secret travels here, never on the broadcast timeline/egress. Values are NOT logged.
    //
    // SECURITY: this RPC lives on the same DigitalBrainGateway service that the browser/Flutter client reaches on
    // the external ingress (CORS is not a security boundary). It is therefore gated by a service-to-service secret:
    // only callers that present the configured InternalServiceKey (the kernel + internal transports — never the
    // browser, which is not given the key) may pull decrypted secrets.
    public override async Task<PackConfigReply> GetPackConfig(GetPackConfigRequest request, ServerCallContext context)
    {
        EnforceInternalCaller(context);

        if (packConfigStore is null)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Pack config store is not configured."));

        var scope = string.IsNullOrWhiteSpace(request.Scope) ? "default" : request.Scope;
        var values = await packConfigStore.GetAsync(scope, request.Pack);

        var reply = new PackConfigReply();
        foreach (var (key, value) in values)
            reply.Values[key] = value;
        return reply;
    }

    // gRPC metadata header carrying the shared service-to-service secret. Lower-case per gRPC ASCII-header rules.
    internal const string InternalKeyHeader = "x-internal-key";

    // Reject any caller that cannot prove it is an internal transport. The kernel is configured with a shared
    // InternalServiceKey (injected as an env param to both the kernel and the internal transport); the transport
    // presents it as the x-internal-key metadata header. Constant-time compare avoids leaking the key by timing.
    // When NO key is configured: allow only in Development (local "clone + run" convenience), deny otherwise — so a
    // misconfigured production kernel fails closed rather than exposing secrets to the open ingress.
    private void EnforceInternalCaller(ServerCallContext context)
    {
        var configuredKey = configuration["DigitalBrain:InternalServiceKey"];

        if (string.IsNullOrEmpty(configuredKey))
        {
            if (environment.IsDevelopment())
                return;
            logger.LogError("GetPackConfig denied: no InternalServiceKey configured outside Development.");
            throw new RpcException(new Status(StatusCode.Unauthenticated, "internal only"));
        }

        var presented = context.RequestHeaders.GetValue(InternalKeyHeader);
        if (presented is null || !FixedTimeEquals(presented, configuredKey))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "internal only"));
    }

    private static bool FixedTimeEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

    // Surface-action payloads arrive from both Flutter (camelCase) and test/native callers (PascalCase).
    // A case-insensitive view lets one set of key lookups serve both without silent misses.
    private static Dictionary<string, object?> CaseInsensitive(Dictionary<string, object?>? source) =>
        new(source ?? new(), StringComparer.OrdinalIgnoreCase);

    // STJ deserializes JSON numbers/booleans as JsonElement when the target type is object?.
    // Unwrap them to CLR primitives so Signal consumers read int/long/double/bool/string directly.
    private static Dictionary<string, object?> NormalizeJsonProps(Dictionary<string, object?> raw)
    {
        var result = new Dictionary<string, object?>(raw.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw)
        {
            result[key] = value is System.Text.Json.JsonElement el ? UnwrapElement(el) : value;
        }
        return result;
    }

    private static Dictionary<string, object?> PayloadProps(SynapseEnvelope request)
    {
        var payloadJson = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson) ?? new();
        return NormalizeJsonProps(raw);
    }

    private static object? UnwrapElement(System.Text.Json.JsonElement el) => el.ValueKind switch
    {
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null,
        System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        System.Text.Json.JsonValueKind.Object => el.GetRawText(),
        System.Text.Json.JsonValueKind.Array => el.GetRawText(),
        _ => el.GetString()
    };

    private static Task WriteCardAsync(
        IServerStreamWriter<RfwCardEnvelope> responseStream,
        RfwCard card) =>
        responseStream.WriteAsync(new RfwCardEnvelope
        {
            LibraryName = card.LibraryName,
            RootWidget = card.RootWidget,
            DataJson = card.DataJson,
            CorrelationId = card.CorrelationId ?? string.Empty,
            Timestamp = card.Timestamp.ToString("O"),
            CallerNeuronType = card.Sender?.Value ?? string.Empty
        });

    public override Task<HealthReply> Health(HealthRequest request, ServerCallContext context) =>
        Task.FromResult(new HealthReply
        {
            Ok = true,
            LlmMode = configuration["DigitalBrain:Llm:Provider"] ?? "none"
        });

    public override async Task<AskReply> Ask(AskRequest request, ServerCallContext context)
    {
        var neuronId = string.IsNullOrWhiteSpace(request.NeuronId) ? "ino-main" : request.NeuronId;
        if (neuronId != "ino-main")
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Ask currently supports only 'ino-main'."));

        try
        {
            var ino = grains.GetGrain<IInoNeuron>(neuronId);
            return new AskReply { Text = await ino.AskAsync(request.Prompt) };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ask failed for {NeuronId}", neuronId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    public override async Task<FireReply> Fire(FireRequest request, ServerCallContext context)
    {
        try
        {
            var neuron = NeuronResolver.Resolve(grains, request.NeuronId);
            await neuron.FireAsync(new DemoMessageSynapse(request.Text));
            return new FireReply { Accepted = true };
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fire failed for {NeuronId}", request.NeuronId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    public override async Task<TimelineReply> Timeline(TimelineRequest request, ServerCallContext context)
    {
        var max = request.MaxEntries <= 0 ? 10 : request.MaxEntries;
        INeuron neuron;
        try
        {
            neuron = NeuronResolver.Resolve(grains, request.NeuronId);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        try
        {
            var timeline = await neuron.GetTimelineAsync();
            var reply = new TimelineReply();
            foreach (var s in timeline.TakeLast(max))
            {
                reply.Entries.Add(new TimelineEntry
                {
                    Type = s.Type,
                    Timestamp = s.Timestamp.ToString("O"),
                    Text = s is DemoMessageSynapse demo ? demo.Text : (s.ToString() ?? string.Empty)
                });
            }
            return reply;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Timeline failed for {NeuronId}", request.NeuronId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    private async Task InstallAndRunSurfaceDemoAsync(string correlationId)
    {
        var requestCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId;

        var pack = SurfaceDemoRuntime.SignedPack();
        var marketplace = grains.GetGrain<IMarketplaceNeuron>("market-ui-demo");
        var generated = grains.GetGrain<IGeneratedNeuron>(SurfaceDemoRuntime.GeneratedNeuronKey);

        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "request accepted");

        await marketplace.FireAsync(new PublishToMarketplace(
            pack.Name,
            pack.Version,
            pack.Code,
            pack.OwnerId,
            pack.IsPrivate,
            pack.CommissionRate,
            pack.Description,
            pack.AuthorPublicKeyBase64,
            pack.SignatureBase64)
        {
            CorrelationId = requestCorrelationId
        });

        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "signed pack published to marketplace");

        await marketplace.FireAsync(new InstallFromMarketplace(pack.Name, pack.Version, BuyerId: "flutter-demo")
        {
            CorrelationId = requestCorrelationId
        });

        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "pack installed into generated neuron");

        var demoText = string.IsNullOrWhiteSpace(correlationId)
            ? "flutter-live-demo"
            : correlationId;
        await generated.FireAsync(new DemoMessageSynapse(demoText)
        {
            CorrelationId = requestCorrelationId
        });

        var generatedTimeline = await generated.GetOutgoingTimelineAsync();
        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "journaled response and surface update observed", generatedTimeline);
    }

    private async Task PublishSurfaceDemoGraphAsync(
        string correlationId,
        string phase,
        IReadOnlyList<Synapse>? generatedTimeline = null)
    {
        var surface = SurfaceDemoRuntime.ActivityGraphSurface(correlationId, phase, generatedTimeline);
        var observability = grains.GetGrain<IObservabilityNeuron>(SurfaceDemoRuntime.ObservabilityNeuronKey);
        try
        {
            await observability.FireAsync(surface);
            logger.LogInformation("Published journaled surface demo graph phase={Phase} correlation={CorrelationId}", phase, correlationId);
        }
        catch (Exception ex) when (IsObservabilityJournalUnavailable(ex))
        {
            logger.LogWarning(ex, "Observability neuron unavailable; streaming graph surface without blocking phase={Phase} correlation={CorrelationId}", phase, correlationId);
            homeFeedBus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, "digitalbrain.gateway"));
        }
    }

    private static bool IsObservabilityJournalUnavailable(Exception exception) =>
        exception.GetBaseException().Message.Contains("state journal stream writer is not initialized", StringComparison.OrdinalIgnoreCase);
}

