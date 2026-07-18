using System.Text.Json;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Runtime.Visualization;
using DigitalBrain.Kernel.Cortex;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Kernel.User;
using SynapseEnvelope = DigitalBrain.Runtime.Grpc.SynapseEnvelope;
using DigitalBrain.Kernel.Visualization;
using Google.Protobuf;
using Grpc.Core;
using DigitalBrain.Runtime.Diagnostics;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Streams;
using DigitalBrain.SDK.DigitalBrain.Ai;

namespace DigitalBrain.Kernel.Gateway;

public sealed class DigitalBrainGatewayService(
    SynapsePayloadRegistry registry,
    GatewayCorrelationTracker tracker,
    HomeFeedBus homeFeed,
    IGrainFactory grains,
    IAiHealthProbe aiHealthProbe,
    SystemHookEmitter systemHooks,
    IClusterClient clusterClient,
    IFlutterPerfHintBroadcaster hintBroadcaster,
    ILogger<DigitalBrainGatewayService> logger) : DigitalBrainGateway.DigitalBrainGatewayBase
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public override async Task<SynapseEnvelope> Send(SynapseEnvelope request, ServerCallContext ctx)
    {
        var activeUser = "anonymous";
        var sessionTokenForUserFlow = ctx.RequestHeaders.FirstOrDefault(h => h.Key.Equals("x-session-token", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(sessionTokenForUserFlow))
        {
            try
            {
                var userFlowGrainId = GrainId.Create(GrainType.Create("DigitalBrain.SDK.Identity.IdentityStore"), "DigitalBrain.SDK.Identity.IdentityStore");
                var userFlowIdentityStore = grains.GetGrain<ICallNeuronTarget>(userFlowGrainId);
                var validationResult = await userFlowIdentityStore.AskAsync($"validate-token {sessionTokenForUserFlow}");
                if (!string.IsNullOrEmpty(validationResult) && validationResult.StartsWith("valid:", StringComparison.Ordinal))
                {
                    activeUser = validationResult.Substring("valid:".Length);
                }
            }
            catch
            {
                // Fallback to anonymous on validation errors
            }
        }
        RequestContext.Set("DigitalBrain.ActiveUser", activeUser);

        var scope = GetRequiredBrainId(ctx.RequestHeaders, request.TypeName);
        var activeScope = scope;
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, activeScope);

        var isGlobal = string.IsNullOrEmpty(activeScope) || activeScope.Equals(BrainScopeHelper.GlobalScope, StringComparison.OrdinalIgnoreCase);
        if (isGlobal)
        {
            var isBootstrapSynapse = request.TypeName == "DigitalBrain.SDK.Identity.Contracts.RequestLogin" ||
                                     request.TypeName == "DigitalBrain.SDK.Identity.Contracts.RequestLoginCard" ||
                                     request.TypeName == "DigitalBrain.SDK.Identity.Contracts.RequestCreateBrain" ||
                                     request.TypeName == "DigitalBrain.Domains.Onboarding.Contracts.RequestOnboarding" ||
                                     request.TypeName == "DigitalBrain.Domains.Onboarding.Contracts.AcceptPolicy" ||
                                     request.TypeName == "DigitalBrain.Domains.Onboarding.Contracts.PolicyAccepted";

            if (!isBootstrapSynapse)
            {
                var sessionToken = ctx.RequestHeaders.FirstOrDefault(h => h.Key.Equals("x-session-token", StringComparison.OrdinalIgnoreCase))?.Value;
                if (string.IsNullOrEmpty(sessionToken))
                {
                    throw new RpcException(new Status(StatusCode.Unauthenticated, "Global Brain access requires login / brain-sync."));
                }

                var grainId = GrainId.Create(GrainType.Create("DigitalBrain.SDK.Identity.IdentityStore"), "DigitalBrain.SDK.Identity.IdentityStore");
                var identityStore = grains.GetGrain<ICallNeuronTarget>(grainId);
                var validationResult = await identityStore.AskAsync($"validate-token {sessionToken}");

                if (string.IsNullOrEmpty(validationResult) || !validationResult.StartsWith("valid:", StringComparison.Ordinal))
                {
                    throw new RpcException(new Status(StatusCode.Unauthenticated, "Global Brain access requires login / brain-sync."));
                }
            }
        }

        if (!registry.TryResolve(request.TypeName, out var synapseType))
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Unknown synapse type '{request.TypeName}'."));
        if (!typeof(Synapse).IsAssignableFrom(synapseType))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Type '{synapseType.FullName}' is not a Synapse subtype."));

        var incoming = (Synapse?)JsonSerializer.Deserialize(request.Payload.Span, synapseType, JsonOptions)
            ?? throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Payload deserialized to null."));

        var correlationId = string.IsNullOrEmpty(request.CorrelationId)
            ? Guid.NewGuid()
            : Guid.Parse(request.CorrelationId);

        var stamped = incoming with
        {
            SynapseId = incoming.SynapseId == default ? Guid.NewGuid() : incoming.SynapseId,
            CorrelationId = correlationId,
            CallerNeuronId = GatewayNeuron.GatewayInstanceKey,
            CallerNeuronType = GatewayNeuron.GatewayNeuronType,
            ReceiverNeuronId = incoming.ReceiverNeuronId == default ? correlationId : incoming.ReceiverNeuronId,
            Timestamp = incoming.Timestamp == default ? TimeProvider.System.GetUtcNow() : incoming.Timestamp,
        };

        stamped = DigitalBrainTelemetry.CaptureTraceContext(stamped);

        RequestContext.Set("DigitalBrain.CorrelationId", correlationId);
        using var awaiter = tracker.Track(correlationId, ctx.CancellationToken);
        var gateway = grains.GetGrain<IGatewayNeuron>(GatewayNeuron.GatewayInstanceKey);
        await gateway.RouteAsync(stamped);

        Synapse reply;
        try
        {
            reply = await awaiter.Task;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Gateway timed out waiting for reply to {SynapseType} (correlation {CorrelationId}).",
                request.TypeName, correlationId);
            throw new RpcException(new Status(StatusCode.DeadlineExceeded,
                $"No reply for synapse '{request.TypeName}' (correlation {correlationId}) within the configured timeout."));
        }

        var replyType = reply.GetType();
        return new SynapseEnvelope
        {
            CorrelationId = reply.CorrelationId.ToString(),
            TypeName = replyType.FullName ?? "",
            Payload = ByteString.CopyFrom(
                JsonSerializer.SerializeToUtf8Bytes(reply, replyType, JsonOptions)),
        };
    }

    public override async Task<TranscribeResponse> Transcribe(
        IAsyncStreamReader<TranscribeRequest> requestStream,
        ServerCallContext ctx)
    {
        var scope = GetRequiredBrainId(ctx.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, scope);

        string mimeType = "audio/wav";
        string? languageHint = null;
        var audio = new MemoryStream();

        await foreach (var chunk in requestStream.ReadAllAsync(ctx.CancellationToken))
        {
            if (audio.Length == 0)
            {
                if (!string.IsNullOrEmpty(chunk.MimeType)) mimeType = chunk.MimeType;
                if (!string.IsNullOrEmpty(chunk.LanguageHint)) languageHint = chunk.LanguageHint;
            }
            if (!chunk.AudioChunk.IsEmpty)
                audio.Write(chunk.AudioChunk.Span);
        }

        var correlationId = Guid.NewGuid();
        var request = new Voice2TextRequest(Audio: audio.ToArray(),
        MimeType: mimeType,
        LanguageHint: languageHint,
        ReturnSegments: false) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: correlationId,
            causationId: null,
            callerNeuronId: GatewayNeuron.GatewayInstanceKey,
            callerNeuronType: GatewayNeuron.GatewayNeuronType,
            receiverNeuronId: correlationId,
            receiverNeuronType: "Voice2TextNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) };

        var routed = DigitalBrainTelemetry.CaptureTraceContext(request);

        RequestContext.Set("DigitalBrain.CorrelationId", correlationId);
        using var awaiter = tracker.Track(correlationId, ctx.CancellationToken);
        var gateway = grains.GetGrain<IGatewayNeuron>(GatewayNeuron.GatewayInstanceKey);
        await gateway.RouteAsync(routed);

        Synapse reply;
        try
        {
            reply = await awaiter.Task;
        }
        catch (OperationCanceledException)
        {
            throw new RpcException(new Status(StatusCode.DeadlineExceeded,
                $"No transcript reply for correlation {correlationId} within the configured timeout."));
        }

        if (reply is not Voice2TextResponse v2t)
            throw new RpcException(new Status(StatusCode.Internal,
                $"Expected Voice2TextResponse for correlation {correlationId}, got {reply.GetType().Name}."));

        return new TranscribeResponse
        {
            Transcript = v2t.Transcript,
            DetectedLanguage = v2t.DetectedLanguage,
            CorrelationId = correlationId.ToString(),
        };
    }

    public override async Task WatchHomeFeed(
        WatchHomeFeedRequest request,
        IServerStreamWriter<RfwCardEnvelope> responseStream,
        ServerCallContext ctx)
    {
        var scope = GetRequiredBrainId(ctx.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, scope);

        using var subscription = homeFeed.Subscribe();
        logger.LogInformation("Home feed subscriber attached.");

        // E-RUN #38: v3 §L7 Brain.Started system hook fires the first time
        // the shell attaches to the home feed. SystemHookEmitter handles the
        // Interlocked.CompareExchange so re-attaches are no-ops; this is the
        // narrowest place in the kernel that observes "shell is connected"
        // without crossing into the E4 brain-shell surface.
        await systemHooks.EmitBrainStartedIfFirstAsync(ctx.CancellationToken);

        // Compose the default home dashboard now that a shell is on the live
        // feed. Fire without awaiting so a cold Canvas/Settings neuron can't
        // head-of-line block the read loop below — the composed cards arrive on
        // this subscription's unbounded channel and the loop delivers them as it
        // drains. Idempotent: cards carry stable per-widget correlationIds, so a
        // reconnect refreshes the same panels rather than duplicating them.
        _ = grains.GetGrain<Cortex.IIntentDispatcher>(Guid.Empty)
            .ComposeDashboardAsync()
            .ContinueWith(
                t => logger.LogWarning(t.Exception, "Failed to compose the home dashboard on shell attach."),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        try
        {
            await foreach (var card in subscription.Reader.ReadAllAsync(ctx.CancellationToken))
            {
                await responseStream.WriteAsync(new RfwCardEnvelope
                {
                    CorrelationId = card.CorrelationId.ToString(),
                    LibraryName = card.LibraryName,
                    RootWidget = card.RootWidget,
                    DataJson = card.DataJson,
                    Timestamp = card.Timestamp.ToString("O"),
                    CallerNeuronType = card.CallerNeuronType ?? string.Empty,
                }, ctx.CancellationToken);
            }
        }
        catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Home feed subscriber disconnected.");
        }
    }

    public override async Task<AiHealthResponse> AiHealth(AiHealthRequest request, ServerCallContext ctx)
    {
        var scope = GetRequiredBrainId(ctx.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, scope);

        var status = await aiHealthProbe.InspectAsync();
        return new AiHealthResponse
        {
            Live   = status.Live,
            Reason = status.Reason,
            Model  = status.Model,
        };
    }

    public override async Task<SubmitPromptReply> SubmitPrompt(SubmitPromptRequest req, ServerCallContext ctx)
    {
        RequestContext.Set("DigitalBrain.ActiveUser", req.UserId ?? "anonymous");
        var scope = GetRequiredBrainId(ctx.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, scope);

        var correlationId = Guid.TryParse(req.CorrelationId, out var parsed) && parsed != Guid.Empty
            ? parsed
            : Guid.NewGuid();
        var userId = string.IsNullOrEmpty(req.UserId) ? "default" : req.UserId;
        var user = grains.GetGrain<IUserNeuron>(userId);
        await user.SubmitPromptAsync(req.Text, correlationId, ctx.CancellationToken);
        return new SubmitPromptReply { CorrelationId = correlationId.ToString() };
    }

    public override async Task<FlutterPerfAck> PushFlutterPerf(
        IAsyncStreamReader<FlutterPerfSampleProto> requestStream,
        ServerCallContext context)
    {
        var scope = GetRequiredBrainId(context.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, scope);

        var streamProvider = clusterClient.GetStreamProvider(StreamProviderConfig.SynapseProviderName);
        var stream = streamProvider.GetStream<Synapse>(
            StreamId.Create(FlutterPerfNeuron.FlutterPerfNeuronType, Guid.Empty));

        await foreach (var proto in requestStream.ReadAllAsync(context.CancellationToken))
        {
            if (double.IsNaN(proto.P95FrameMs) || double.IsInfinity(proto.P95FrameMs))
                continue; // reject malformed samples — do not poison the projection

            var sample = new FlutterPerfSample(ClientId:           proto.ClientId,
        SampleWindowId:     proto.SampleWindowId,
        FrameCount:         proto.FrameCount,
        P50FrameMs:         proto.P50FrameMs,
        P95FrameMs:         proto.P95FrameMs,
        JankPct:            proto.JankPct,
        WidgetCount:        proto.WidgetCount,
        GlowPainterCount:   proto.GlowPainterCount,
        RebuildsPerSecond:  proto.RebuildsPerSecond,
        Platform:           proto.Platform) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.NewGuid(),
            causationId: null,
            callerNeuronId: Guid.Empty,
            callerNeuronType: "client/flutter",
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: FlutterPerfNeuron.FlutterPerfNeuronType,
            timestamp: DateTimeOffset.TryParse(proto.Timestamp, out var ts)
                                      ? ts : DateTimeOffset.UtcNow
        ) };

            await stream.OnNextAsync(sample);
        }

        return new FlutterPerfAck();
    }

    public override async Task WatchVisualLoadHint(
        WatchVisualLoadHintRequest request,
        IServerStreamWriter<VisualLoadHintProto> responseStream,
        ServerCallContext context)
    {
        var scope = GetRequiredBrainId(context.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, scope);

        await foreach (var hint in hintBroadcaster.SubscribeAsync(request.ClientId, context.CancellationToken))
        {
            if (context.CancellationToken.IsCancellationRequested) return;
            await responseStream.WriteAsync(new VisualLoadHintProto
            {
                ClientId  = hint.ClientId,
                Tier      = hint.Tier,
                Reason    = hint.Reason,
                Timestamp = hint.Timestamp.ToString("O"),
            });
        }
    }

    public override async Task<GetLatestCardReply> GetLatestCard(
        GetLatestCardRequest request,
        ServerCallContext ctx)
    {
        var scope = GetRequiredBrainId(ctx.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, scope);

        if (string.IsNullOrEmpty(request.NeuronId))
        {
            return new GetLatestCardReply { HasCard = false };
        }

        try
        {
            var store = grains.GetGrain<INeuronRfwStoreGrain>(request.NeuronId);
            var card = await store.GetLatestCardAsync();

            if (card == null)
            {
                // Lazily serve compiled layout description payload from the catalog
                var catalog = grains.GetGrain<IBrainCatalog>(BrainScopeHelper.GlobalScope);
                var entries = await catalog.ListRegisteredAsync();
                var entry = entries.FirstOrDefault(e => string.Equals(e.Id.Value, request.NeuronId, StringComparison.OrdinalIgnoreCase)
                                                     || string.Equals(e.TypeFullName, request.NeuronId, StringComparison.OrdinalIgnoreCase));
                if (entry?.UiLayoutJson != null)
                {
                    return new GetLatestCardReply
                    {
                        Card = new RfwCardEnvelope
                        {
                            CorrelationId = Guid.Empty.ToString(),
                            LibraryName = "uikit",
                            RootWidget = "UiKit",
                            DataJson = entry.UiLayoutJson,
                            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                            CallerNeuronType = request.NeuronId
                        },
                        HasCard = true
                    };
                }

                return new GetLatestCardReply { HasCard = false };
            }

            return new GetLatestCardReply
            {
                Card = new RfwCardEnvelope
                {
                    CorrelationId = card.CorrelationId,
                    LibraryName = card.LibraryName,
                    RootWidget = card.RootWidget,
                    DataJson = card.DataJson,
                    Timestamp = card.Timestamp,
                    CallerNeuronType = card.CallerNeuronType
                },
                HasCard = true
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve persisted RfwCard for neuron {NeuronId}", request.NeuronId);
            return new GetLatestCardReply { HasCard = false };
        }
    }

    public override async Task<RfwLayoutReply> GetRfwLayout(
        RfwLayoutRequest request,
        ServerCallContext ctx)
    {
        var scope = GetRequiredBrainId(ctx.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, scope);

        try
        {
            var uiScene = grains.GetGrain<IUiSceneGrain>("global");
            var (rfwTemplate, dataJson) = await uiScene.GetLayoutAsync(request.LayoutName, request.NeuronId);

            return new RfwLayoutReply
            {
                RfwTemplate = rfwTemplate,
                DataJson = dataJson
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve dynamic RFW layout '{LayoutName}'", request.LayoutName);
            throw new RpcException(new Status(StatusCode.Internal, $"Failed to retrieve layout: {ex.Message}"));
        }
    }

    private string GetRequiredBrainId(Metadata requestHeaders, string? synapseTypeName = null)
    {
        var brainId = requestHeaders.FirstOrDefault(h => h.Key.Equals("x-brain-id", StringComparison.OrdinalIgnoreCase))?.Value
            ?? requestHeaders.FirstOrDefault(h => h.Key.Equals("x-active-scope", StringComparison.OrdinalIgnoreCase))?.Value;

        if (string.IsNullOrEmpty(brainId))
        {
            var isBootstrap = synapseTypeName == "DigitalBrain.SDK.Identity.Contracts.RequestLogin" ||
                              synapseTypeName == "DigitalBrain.SDK.Identity.Contracts.RequestLoginCard" ||
                              synapseTypeName == "DigitalBrain.SDK.Identity.Contracts.RequestCreateBrain" ||
                              synapseTypeName == "DigitalBrain.Domains.Onboarding.Contracts.RequestOnboarding" ||
                              synapseTypeName == "DigitalBrain.Domains.Onboarding.Contracts.AcceptPolicy" ||
                              synapseTypeName == "DigitalBrain.Domains.Onboarding.Contracts.PolicyAccepted";

            if (isBootstrap)
            {
                return BrainScopeHelper.GlobalScope;
            }

            throw new RpcException(new Status(StatusCode.InvalidArgument, "Active brain ID (x-brain-id) header is required for brain-scoped operations."));
        }

        return brainId;
    }
}
