using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Navigator;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Diagnostics;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Streams;
using Orleans.Concurrency;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace DigitalBrain.Kernel.Cortex;

[Reentrant]
[GrainType("GatewayNeuron")]
[ImplicitStreamSubscription(GatewayNeuronType)]
[ImplicitStreamSubscription("External")]
public sealed class GatewayNeuron(
    GatewayCorrelationTracker tracker,
    NavigatorRouter router,
    ILogger<GatewayNeuron> logger)
    : Grain, IGatewayNeuron, IStreamSubscriptionObserver, IAsyncObserver<Synapse>
{
    public const string GatewayNeuronType = nameof(GatewayNeuron);
    public static readonly Guid GatewayInstanceKey = Guid.Empty;

    public async Task RouteAsync(Synapse synapse)
    {
        var handler = await router.ResolveHandlerAsync(synapse.GetType().FullName!, synapse.ReceiverNeuronType);

        using var activity = DigitalBrainTelemetry.StartLinkedActivity(
            DigitalBrainTelemetry.NavigatorRoute, synapse);

        synapse = DigitalBrainTelemetry.CaptureTraceContext(synapse);

        var streamProvider = this.GetStreamProvider(StreamProviderConfig.SynapseProviderName);

        if (handler.IsInterpreted)
        {
            var dynamicGrain = this.GrainFactory.GetGrain<IDynamicNeuron>(handler.TypeFullName);
            var payloadJson = System.Text.Json.JsonSerializer.Serialize(synapse, synapse.GetType(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var correlationId = new global::DigitalBrain.Runtime.CorrelationId(synapse.CorrelationId.ToString());

            _ = Task.Run(async () =>
            {
                try
                {
                    await dynamicGrain.InvokeAsync(payloadJson, synapse.GetType().FullName!, correlationId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to invoke dynamic interpreted grain {Fqn}", handler.TypeFullName);
                }
            });

            var timelineStream = streamProvider.GetStream<Synapse>(
                StreamId.Create(Neuron.GlobalTimelineNamespace, Guid.Empty));
            await timelineStream.OnNextAsync(synapse);

            logger.LogDebug(
                "Gateway routed interpreted {SynapseType} corr={CorrelationId} to IDynamicNeuron/{Fqn}",
                synapse.GetType().Name, synapse.CorrelationId, handler.TypeFullName);
            return;
        }

        var receiverNamespace = NavigatorRouter.ImplicitSubscriptionNamespace(handler);
        var stream = streamProvider.GetStream<Synapse>(
            StreamId.Create(receiverNamespace, synapse.ReceiverNeuronId));
        await stream.OnNextAsync(synapse);
        var dispatchTarget = receiverNamespace + "/" + synapse.ReceiverNeuronId;

        var timeline = streamProvider.GetStream<Synapse>(
            StreamId.Create(Neuron.GlobalTimelineNamespace, Guid.Empty));
        await timeline.OnNextAsync(synapse);

        logger.LogDebug(
            "Gateway routed {SynapseType} corr={CorrelationId} to {Target}",
            synapse.GetType().Name, synapse.CorrelationId, dispatchTarget);
    }

    public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        var handle = handleFactory.Create<Synapse>();
        try
        {
            await handle.ResumeAsync(this);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Resuming subscription with cached token failed in GatewayNeuron. Falling back to fresh subscribe.");
            await handle.ResumeAsync(this, null);
        }
    }

    public Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        using var activity = DigitalBrainTelemetry.StartLinkedActivity(
            DigitalBrainTelemetry.GatewayReply, item);

        if (item != null)
        {
            logger.LogInformation("GatewayNeuron: Received synapse reply in stream! Type = {Type}, CorrelationId = {CorrelationId}, ReceiverNeuronType = {ReceiverNeuronType}", 
                item.GetType().FullName, item.CorrelationId, item.Headers?.ReceiverNeuronType);
        }

        tracker.Complete(item!);
        return Task.CompletedTask;
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        if (ex is QueueCacheMissException || ex.GetType().FullName == "Orleans.Streams.QueueCacheMissException")
        {
            logger.LogWarning(ex, "Transient stream cache miss in GatewayNeuron; Orleans pulling agent will recover.");
        }
        else
        {
            logger.LogError(ex, "Gateway stream subscription error.");
        }
        return Task.CompletedTask;
    }

    public Task EnsureActivatedAsync() => Task.CompletedTask;
}
