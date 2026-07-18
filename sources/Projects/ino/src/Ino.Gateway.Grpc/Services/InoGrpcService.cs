using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Ino.Core;
using Ino.Core.Brain;
using Ino.Core.Hosting.Brain;
using Ino.Gateway;
using Ino.Grpc;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.Logging;

namespace Ino.Gateway.Grpc.Services;

using WireInoEvent = global::Ino.Grpc.InoEvent;
using GatewayInoEvent = global::Ino.Core.Hosting.InoEvent;

/// <summary>
/// gRPC surface that maps the wire contract (<c>ino.v1.Ino</c>) onto
/// <see cref="IInoGateway"/>. Chat is a server-streaming RPC so the gateway
/// can emit incremental frames: first a skeleton placeholder card set
/// (<see cref="ChatResult.IsSkeleton"/> = true) that the Flutter client
/// paints immediately as shimmer bars, then a final frame with the neuron
/// handler's real result. Other RPCs inherited from <c>Ino.InoBase</c>
/// auto-return <c>StatusCode.Unimplemented</c> until later slices replace
/// them.
/// </summary>
public sealed class InoGrpcService(
    IInoGateway gateway,
    BrainPulseHub brainPulseHub,
    ILogger<InoGrpcService> log) : global::Ino.Grpc.Ino.InoBase
{
    public override async Task Chat(
        ChatRequest request,
        IServerStreamWriter<ChatResponse> responseStream,
        ServerCallContext context)
    {
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId;
        var clientCorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? null : request.CorrelationId;
        var frameIndex = 0;

        await foreach (var result in gateway.ChatAsync(request.Message, userId, clientCorrelationId, context.CancellationToken))
        {
            var response = new ChatResponse
            {
                Reply = result.Reply,
                NeuronId = result.NeuronId,
                ContentType = result.ContentType,
                IsSkeleton = result.IsSkeleton,
                CorrelationId = result.CorrelationId,
            };

            if (!result.RfwDescription.IsEmpty)
                response.RfwDescription = StripCarriageReturns(result.RfwDescription.Span);
            if (!result.RfwData.IsEmpty)
                response.RfwData = StripCarriageReturns(result.RfwData.Span);

            log.LogDebug(
                "chat frame {Frame}: user={UserId} correlation={CorrelationId} neuron={Neuron} contentType={ContentType} skeleton={Skeleton}",
                frameIndex, userId, result.CorrelationId, result.NeuronId, response.ContentType, response.IsSkeleton);

            await responseStream.WriteAsync(response, context.CancellationToken);
            frameIndex++;
        }
    }

    public override async Task<FireResponse> FireSynapse(
        FireRequest request,
        ServerCallContext context)
    {
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId;
        var args = request.Args.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        try
        {
            var fire = await gateway.FireSynapseAsync(
                request.Verb,
                args,
                request.CorrelationId,
                userId,
                context.CancellationToken);

            var response = new FireResponse
            {
                Ok = fire.Success,
                SynapseId = fire.SynapseId,
                Reply = fire.Reply,
                ContentType = fire.ContentType,
                CorrelationId = fire.CorrelationId,
            };
            if (!fire.RfwDescription.IsEmpty)
                response.RfwDescription = StripCarriageReturns(fire.RfwDescription.Span);
            if (!fire.RfwData.IsEmpty)
                response.RfwData = StripCarriageReturns(fire.RfwData.Span);
            return response;
        }
        catch (NotSupportedException ex)
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, ex.Message));
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    public override async Task StreamEvents(
        EventSubscription request,
        IServerStreamWriter<WireInoEvent> responseStream,
        ServerCallContext context)
    {
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId;
        var filter = request.EventTypes.Count == 0 ? null : request.EventTypes.ToList();

        log.LogInformation("stream-events opened: user={UserId} types={Types}",
            userId, filter is null ? "<all>" : string.Join(",", filter));

        await foreach (var evt in gateway.StreamEventsAsync(userId, filter, context.CancellationToken))
        {
            var wire = new WireInoEvent
            {
                Type = evt.Type,
                SourceNeuron = evt.SourceNeuron,
                Timestamp = evt.TimestampUnixMs,
            };
            if (!evt.Payload.IsEmpty)
                wire.Payload = ByteString.CopyFrom(evt.Payload.Span);
            await responseStream.WriteAsync(wire, context.CancellationToken);
        }
    }

    public override async Task<JournalResponse> GetJournal(
        JournalRequest request,
        ServerCallContext context)
    {
        var limit = request.Limit <= 0 ? 50 : request.Limit;
        var neuron = string.IsNullOrWhiteSpace(request.NeuronId) ? null : request.NeuronId;
        var entries = await gateway.GetJournalAsync(neuron, limit, context.CancellationToken);

        var response = new JournalResponse();
        foreach (var entry in entries)
        {
            response.Entries.Add(new JournalEntry
            {
                Timestamp = entry.TimestampUnixMs,
                Kind = entry.Kind,
                SynapseVerb = entry.SynapseVerb,
                CorrelationId = entry.CorrelationId,
                SourceNeuron = entry.SourceNeuron,
                TargetNeuron = entry.TargetNeuron,
            });
        }
        return response;
    }

    public override async Task<MetricsResponse> GetMetrics(
        MetricsRequest request,
        ServerCallContext context)
    {
        var neuron = string.IsNullOrWhiteSpace(request.NeuronId) ? null : request.NeuronId;
        var snapshot = await gateway.GetMetricsAsync(neuron, context.CancellationToken);

        var response = new MetricsResponse();
        foreach (var metric in snapshot.PerNeuron)
        {
            response.PerNeuron.Add(new NeuronMetric
            {
                NeuronId = metric.NeuronId,
                FireCount = metric.FireCount,
                BroadcastCount = metric.BroadcastCount,
                LastActivatedUnixMs = metric.LastActivatedUnixMs,
            });
        }
        return response;
    }

    public override async Task<ReasoningResponse> GetReasoning(
        ReasoningRequest request,
        ServerCallContext context)
    {
        var reasoning = await gateway.GetReasoningAsync(request.NeuronId, context.CancellationToken);
        return new ReasoningResponse
        {
            NeuronId = reasoning.NeuronId,
            Source = reasoning.Source,
            ScenarioName = reasoning.ScenarioName,
            Text = reasoning.Text,
        };
    }

    // ── Inspector E.3 — Slice 3B ──────────────────────────────────────────────

    public override async Task<ListProposalsResponse> ListProposals(
        ListProposalsRequest request,
        ServerCallContext context)
    {
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId;
        ProposalStatus? filter = request.HasFilter ? (ProposalStatus)request.Filter : null;
        var skip = request.Skip >= 0 ? request.Skip : 0;
        var take = request.Take > 0 ? request.Take : 50;
        var entries = await gateway.ListProposalsAsync(userId, filter, skip, take, context.CancellationToken);
        var resp = new ListProposalsResponse();
        resp.Entries.AddRange(entries.Select(ToProposalView));
        return resp;
    }

    public override async Task<DecideProposalResponse> DecideProposal(
        DecideProposalRequest request,
        ServerCallContext context)
    {
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId;
        try
        {
            await gateway.DecideProposalAsync(
                userId,
                request.ProposalId,
                (ProposalStatus)request.Decision,
                context.CancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        return new DecideProposalResponse { Accepted = true };
    }

    public override async Task<ListRoutingDecisionsResponse> ListRoutingDecisions(
        ListRoutingDecisionsRequest request,
        ServerCallContext context)
    {
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId;
        var count = request.Count <= 0 ? 20 : Math.Min(request.Count, 20);
        var entries = await gateway.ListRoutingDecisionsAsync(userId, count, context.CancellationToken);
        var resp = new ListRoutingDecisionsResponse();
        resp.Entries.AddRange(entries.Select(ToRoutingDecisionView));
        return resp;
    }

    public override async Task<AskInoResponse> AskIno(AskInoRequest request, ServerCallContext context)
    {
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId;
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? InoNeuronGrainKey.DefaultSessionId : request.SessionId;
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? null : request.CorrelationId;

        var ino = await gateway.AskAsync(request.Prompt, userId, sessionId, correlationId, context.CancellationToken);

        var response = new AskInoResponse
        {
            Success = ino.Success,
            Reply = ino.Text,
            CorrelationId = ino.CorrelationId,
            Source = ino.Source ?? string.Empty,
        };
        if (ino.Rfw is { } payload)
        {
            response.RfwDescription = StripCarriageReturns(payload.DescriptionDsl);
            response.RfwData = StripCarriageReturns(payload.DataPayload);
            response.ContentType = $"rfw/{payload.LibraryName}";
        }
        return response;
    }

    public override async Task<RfwEventResponse> RfwEvent(
        RfwEventRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "correlation_id is required"));
        if (string.IsNullOrWhiteSpace(request.EventName))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "event_name is required"));

        var args = request.Args.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var result = await gateway.HandleRfwEventAsync(
            request.CorrelationId,
            request.EventName,
            args,
            context.CancellationToken);

        var response = new RfwEventResponse
        {
            Accepted = result.Success,
            Reply = result.Message ?? string.Empty,
            CorrelationId = request.CorrelationId,
            ContentType = "text",
        };

        if (result.Rfw is { } payload)
        {
            response.RfwDescription = StripCarriageReturns(payload.DescriptionDsl);
            response.RfwData = StripCarriageReturns(payload.DataPayload);
            response.ContentType = $"rfw/{payload.LibraryName}";
        }

        return response;
    }

    private static ProposalView ToProposalView(ProposalEntry e)
    {
        var view = new ProposalView
        {
            ProposalId = e.ProposalId,
            ClusterKey = e.ClusterKey,
            ExamplePrompt = e.ExamplePrompt,
            Occurrences = e.Occurrences,
            ProposedAt = Timestamp.FromDateTimeOffset(e.ProposedAt),
            Status = (ProposalStatusProto)e.Status,
        };
        if (e.ActivatedNeuronId is not null)
            view.ActivatedNeuronId = e.ActivatedNeuronId;
        if (e.DecidedAt is { } decidedAt)
            view.DecidedAt = Timestamp.FromDateTimeOffset(decidedAt);
        return view;
    }

    // RFW text parser (package:rfw 1.1.x) only treats SPACE (0x20) and LF (0x0A)
    // as whitespace; CR (0x0D) hits the default branch and throws ParserException.
    // Verified against flutter/packages main, lib/src/dart/text.dart (R2 in
    // docs/rfw-research-notes.md). Strip on the wire-write path so neuron authors
    // can use ordinary AppendLine / interpolated strings without worrying about
    // platform line endings.
    public static ByteString StripCarriageReturns(ReadOnlySpan<byte> input)
    {
        var crIndex = input.IndexOf((byte)'\r');
        if (crIndex < 0)
            return ByteString.CopyFrom(input);

        var dst = new byte[input.Length];
        int j = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] != (byte)'\r')
                dst[j++] = input[i];
        }
        return ByteString.CopyFrom(dst, 0, j);
    }

    private static RoutingDecisionView ToRoutingDecisionView(RoutingDecision d)
    {
        var view = new RoutingDecisionView
        {
            Prompt = d.Prompt,
            Source = (RoutingSourceProto)d.Source,
            At = Timestamp.FromDateTimeOffset(d.At),
            LlmCalled = d.LlmCalled,
            RoutingDurationMs = d.RoutingDurationMs,
            CorrelationId = d.CorrelationId,
        };
        if (d.NeuronId is not null)
            view.NeuronId = d.NeuronId;
        if (d.Confidence is { } conf)
            view.Confidence = conf;
        if (d.MlPrediction is { } mlPred)
            view.MlPrediction = mlPred;
        if (d.MlConfidence is { } mlConf)
            view.MlConfidence = mlConf;
        return view;
    }

    // ── Inspector debug fire — Slice C.4 ──────────────────────────────────────

    public override async Task<FireTestSynapseResponse> FireTestSynapse(
        FireTestSynapseRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.SynapseType))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "synapse_type is required"));

        try
        {
            var outcome = await gateway.FireTestSynapseAsync(
                request.SynapseType,
                request.PayloadJson ?? string.Empty,
                request.SourceNodeId ?? string.Empty,
                userId: "inspector",
                context.CancellationToken);

            return new FireTestSynapseResponse
            {
                Ok = outcome.Success,
                Error = outcome.Reply ?? string.Empty,
            };
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (NotSupportedException ex)
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, ex.Message));
        }
    }

    // ── Brain trace stream — Slice C.3.9 ─────────────────────────────────────

    public override async Task WatchBrainActivity(
        BrainWatchRequest request,
        IServerStreamWriter<BrainPulseProto> responseStream,
        ServerCallContext context)
    {
        var userFilter = string.IsNullOrWhiteSpace(request.UserIdFilter) ? null : request.UserIdFilter;
        var sessionFilter = string.IsNullOrWhiteSpace(request.SessionIdFilter) ? null : request.SessionIdFilter;

        log.LogInformation(
            "WatchBrainActivity opened: userFilter={UserFilter} sessionFilter={SessionFilter}",
            userFilter ?? "<none>", sessionFilter ?? "<none>");

        var reader = brainPulseHub.Subscribe(context.CancellationToken);
        try
        {
            await foreach (var pulse in reader.ReadAllAsync(context.CancellationToken))
            {
                if (userFilter is not null && !string.Equals(pulse.UserId, userFilter, StringComparison.Ordinal))
                    continue;
                if (sessionFilter is not null && !string.Equals(pulse.InoInstanceId, sessionFilter, StringComparison.Ordinal))
                    continue;

                await responseStream.WriteAsync(MapPulse(pulse), context.CancellationToken);
            }
        }
        catch (OperationCanceledException) { /* client closed stream */ }
    }

    private static BrainPulseProto MapPulse(BrainPulse pulse) => new()
    {
        TraceParent = pulse.TraceParent,
        InoInstanceId = pulse.InoInstanceId,
        UserId = pulse.UserId,
        FromGrain = pulse.FromGrain,
        ToGrain = pulse.ToGrain,
        MethodName = pulse.MethodName,
        DurationMs = pulse.DurationMs,
        Status = pulse.Status switch
        {
            BrainPulseStatus.Ok => BrainPulseStatusProto.BrainPulseStatusOk,
            BrainPulseStatus.Failed => BrainPulseStatusProto.BrainPulseStatusFailed,
            _ => BrainPulseStatusProto.BrainPulseStatusOk,
        },
        TimestampUnixMs = pulse.TimestampUnixMs,
        PayloadJson = pulse.PayloadJson ?? string.Empty,
    };
}
