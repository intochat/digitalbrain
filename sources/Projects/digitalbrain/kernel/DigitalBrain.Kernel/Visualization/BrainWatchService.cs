using System.Text.Json;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Catalog;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Runtime.Neurons;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Orleans.Streams;

namespace DigitalBrain.Kernel.Visualization;

public sealed class BrainWatchService(
    IClusterClient cluster,
    INeuronFeatureLoader features,
    ILogger<BrainWatchService> logger) : BrainWatch.BrainWatchBase
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private async Task<IReadOnlyList<NeuronCatalogEntry>> GetCombinedCatalogAsync(string activeScope)
    {
        var catalog = cluster.GetGrain<IBrainCatalog>("global");
        var registered = await catalog.ListRegisteredAsync();
        if (!activeScope.Equals(BrainScopeHelper.GlobalScope, StringComparison.OrdinalIgnoreCase))
        {
            var privateCatalog = cluster.GetGrain<IBrainCatalog>(activeScope);
            var privateRegistered = await privateCatalog.ListRegisteredAsync();
            registered = privateRegistered.Concat(registered).ToList();
        }
        return registered;
    }

    public override async Task<SnapshotResponse> Snapshot(SnapshotRequest request, ServerCallContext ctx)
    {
        var activeScope = GetRequiredBrainId(ctx.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, activeScope);

        var since = request.Since?.ToDateTimeOffset() ?? TimeProvider.System.GetUtcNow().AddDays(-1);
        var registered = await GetCombinedCatalogAsync(activeScope);
        var relay = cluster.GetGrain<IBrainTimelineRelay>(BrainScopeHelper.GetActiveScopeGuid());

        var seen = await relay.ListSeenAsync();
        var activity = await relay.SnapshotAsync(since);
        var slice = await relay.WatchSinceAsync(long.MaxValue);

        // Fetch active correlation IDs from TaskManagerNeuron
        var taskManager = cluster.GetGrain<ITaskManagerNeuron>(BrainScopeHelper.GetActiveScopeGuid());
        var activeCorrelations = await taskManager.GetActiveCorrelationIdsAsync();
        var activeCorrelationSet = activeCorrelations.ToHashSet();

        // Filter snapshot activity to only include active tasks
        activity = activity.Where(s => activeCorrelationSet.Contains(s.CorrelationId)).ToArray();

        var domains = BuildDomainLookup(registered);
        var response = new SnapshotResponse { Cursor = slice.NextCursor };
        foreach (var n in MergeNodes(registered, seen)) response.Nodes.Add(ToNode(n, domains));
        foreach (var s in activity) response.Edges.Add(ToEdge(s));
        return response;
    }

    public override async Task<WatchSinceResponse> WatchSince(WatchSinceRequest request, ServerCallContext ctx)
    {
        var activeScope = GetRequiredBrainId(ctx.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, activeScope);

        var registered = await GetCombinedCatalogAsync(activeScope);
        var relay = cluster.GetGrain<IBrainTimelineRelay>(BrainScopeHelper.GetActiveScopeGuid());
        var seen = await relay.ListSeenAsync();
        var slice = await relay.WatchSinceAsync(request.Cursor);

        var domains = BuildDomainLookup(registered);
        var response = new WatchSinceResponse { Cursor = slice.NextCursor };
        foreach (var n in MergeNodes(registered, seen)) response.NewNodes.Add(ToNode(n, domains));
        foreach (var s in slice.Records) response.NewEdges.Add(ToEdge(s));
        return response;
    }

    public override async Task<NeuronDetailResponse> GetNeuronDetail(
        NeuronDetailRequest request, ServerCallContext ctx)
    {
        var activeScope = GetRequiredBrainId(ctx.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, activeScope);

        var since = request.Since?.ToDateTimeOffset() ?? TimeProvider.System.GetUtcNow().AddDays(-1);
        var relay = cluster.GetGrain<IBrainTimelineRelay>(BrainScopeHelper.GetActiveScopeGuid());
        var seen = await relay.ListSeenAsync();
        var node = seen.FirstOrDefault(n => n.Id.Value == request.NeuronId);
        if (node is null)
        {
            // Fall back to registered (may exist without ever firing/receiving).
            var registered = await GetCombinedCatalogAsync(activeScope);
            var entry = registered.FirstOrDefault(e => e.TypeFullName == request.NeuronId)
                ?? throw new RpcException(new Status(StatusCode.NotFound,
                    $"No neuron '{request.NeuronId}' has been seen or registered."));
            var now = TimeProvider.System.GetUtcNow();
            node = new CatalogedNeuron(new NeuronId(entry.TypeFullName), now, now);
        }

        var activity = await relay.SnapshotAsync(since);
        var recent = activity.Where(s => s.ReceiverNeuronType == request.NeuronId
                                      || s.CallerNeuronType == request.NeuronId).ToArray();

        var response = new NeuronDetailResponse
        {
            NeuronId = node.Id.Value,
            FirstSeenAt = Timestamp.FromDateTimeOffset(node.FirstSeenAt),
            LastSeenAt = Timestamp.FromDateTimeOffset(node.LastSeenAt),
        };
        foreach (var s in recent) response.Recent.Add(ToEdge(s));
        return response;
    }

    public override async Task<SynapseDetailResponse> GetSynapseDetail(
        SynapseDetailRequest request, ServerCallContext ctx)
    {
        var activeScope = GetRequiredBrainId(ctx.RequestHeaders);
        RequestContext.Set(BrainScopeHelper.ActiveScopeKey, activeScope);

        var relay = cluster.GetGrain<IBrainTimelineRelay>(BrainScopeHelper.GetActiveScopeGuid());
        var activity = await relay.SnapshotAsync(default);
        var chain = activity.Where(s => s.CorrelationId.ToString() == request.CorrelationId).ToArray();

        var response = new SynapseDetailResponse();
        foreach (var s in chain) response.Chain.Add(ToEdge(s));
        return response;
    }

    public override Task<NeuronFeatureResponse> GetNeuronFeature(
        NeuronFeatureRequest request, ServerCallContext ctx)
    {
        var entry = features.GetFeature(request.NeuronId);
        return Task.FromResult(new NeuronFeatureResponse
        {
            FeatureText = entry?.Text ?? "",
            SourceFile = entry?.SourceFile ?? "",
        });
    }

    static Dictionary<string, string> BuildDomainLookup(IReadOnlyList<NeuronCatalogEntry> registered)
        => registered.ToDictionary(e => e.TypeFullName, e => e.Domain, StringComparer.Ordinal);

    static IEnumerable<CatalogedNeuron> MergeNodes(
        IReadOnlyList<NeuronCatalogEntry> registered,
        IReadOnlyList<CatalogedNeuron> seen)
    {
        var bySeen = seen.ToDictionary(n => n.Id.Value, StringComparer.Ordinal);
        var now = TimeProvider.System.GetUtcNow();
        foreach (var entry in registered)
        {
            if (bySeen.TryGetValue(entry.TypeFullName, out var existing))
            {
                yield return existing;
                bySeen.Remove(entry.TypeFullName);
            }
            else
            {
                // Registered but never fired: use placeholder timestamps so the
                // node still draws in the live tab's empty state.
                yield return new CatalogedNeuron(new NeuronId(entry.TypeFullName), now, now);
            }
        }
        // Any remaining "seen" nodes are gateway/synthetic — pass through.
        foreach (var rest in bySeen.Values) yield return rest;
    }

    static NeuronNode ToNode(CatalogedNeuron n, IReadOnlyDictionary<string, string> domains) => new()
    {
        Id = n.Id.Value,
        FirstSeenAt = Timestamp.FromDateTimeOffset(n.FirstSeenAt),
        LastSeenAt = Timestamp.FromDateTimeOffset(n.LastSeenAt),
        Domain = domains.TryGetValue(n.Id.Value, out var d) ? d : "system",
    };

    static SynapseEdge ToEdge(Synapse s)
    {
        var concreteType = s.GetType();
        return new SynapseEdge
        {
            At = Timestamp.FromDateTimeOffset(s.Timestamp),
            FromId = s.CallerNeuronType ?? "",
            ToId = s.ReceiverNeuronType,
            TypeName = concreteType.FullName ?? "",
            MethodName = "",
            CorrelationId = s.CorrelationId.ToString(),
            Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(s, concreteType, JsonOptions)),
        };
    }

    private string GetRequiredBrainId(Metadata requestHeaders)
    {
        var brainId = requestHeaders.FirstOrDefault(h => h.Key.Equals("x-brain-id", StringComparison.OrdinalIgnoreCase))?.Value
            ?? requestHeaders.FirstOrDefault(h => h.Key.Equals("x-active-scope", StringComparison.OrdinalIgnoreCase))?.Value;

        if (string.IsNullOrEmpty(brainId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Active brain ID (x-brain-id) header is required for brain-scoped operations."));
        }

        return brainId;
    }

    public override async Task WatchSynapses(
        WatchSynapsesRequest request,
        IServerStreamWriter<SynapseEdge> responseStream,
        ServerCallContext ctx)
    {
        string activeScope;
        try
        {
            activeScope = GetRequiredBrainId(ctx.RequestHeaders);
        }
        catch
        {
            activeScope = request.BrainId;
        }

        if (string.IsNullOrEmpty(activeScope))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Active brain ID is required."));
        }

        logger.LogInformation("Client subscribed to WatchSynapses for brain scope: {ActiveScope}", activeScope);

        var streamProvider = cluster.GetStreamProvider(Neuron.SynapseStreamProvider);
        var stream = streamProvider.GetStream<Synapse>(
            StreamId.Create(Neuron.GlobalTimelineNamespace, Guid.Empty));

        var channel = System.Threading.Channels.Channel.CreateUnbounded<Synapse>(new System.Threading.Channels.UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        var handle = await stream.SubscribeAsync(
            async (synapse, token) =>
            {
                channel.Writer.TryWrite(synapse);
                await Task.CompletedTask;
            },
            async ex =>
            {
                channel.Writer.TryComplete(ex);
                await Task.CompletedTask;
            },
            async () =>
            {
                channel.Writer.TryComplete();
                await Task.CompletedTask;
            });

        try
        {
            while (await channel.Reader.WaitToReadAsync(ctx.CancellationToken))
            {
                while (channel.Reader.TryRead(out var synapse))
                {
                    if (synapse != null && IsSynapseInScope(synapse, activeScope))
                    {
                        var edge = ToEdge(synapse);
                        await responseStream.WriteAsync(edge);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Client disconnected from WatchSynapses for brain scope: {ActiveScope}", activeScope);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in WatchSynapses for brain scope: {ActiveScope}", activeScope);
            throw;
        }
        finally
        {
            if (handle != null)
            {
                try
                {
                    await handle.UnsubscribeAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to unsubscribe from Orleans timeline stream.");
                }
            }
        }
    }

    private static bool IsSynapseInScope(Synapse synapse, string activeScope)
    {
        if (string.IsNullOrEmpty(activeScope) || activeScope.Equals(BrainScopeHelper.GlobalScope, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(synapse.CallerNeuronType))
        {
            var (scope, _) = BrainScopeHelper.ParseScopedNeuronKey(synapse.CallerNeuronType);
            if (scope.Equals(activeScope, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrEmpty(synapse.ReceiverNeuronType))
        {
            var (scope, _) = BrainScopeHelper.ParseScopedNeuronKey(synapse.ReceiverNeuronType);
            if (scope.Equals(activeScope, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
