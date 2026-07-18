using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Diagnostics;

public static class DigitalBrainTelemetry
{
    public const string SourceName = "DigitalBrain";

    public static readonly ActivitySource Source = new(SourceName);

    public static readonly Meter Meter = new(SourceName);

    public const string MetricSynapsesHandled = "digitalbrain.synapses.handled";
    public const string MetricSynapsesFired = "digitalbrain.synapses.fired";
    public const string MetricNeuronErrors = "digitalbrain.neuron.errors";
    public const string MetricHandleDurationMs = "digitalbrain.neuron.handle.duration.ms";

    static readonly ConcurrentDictionary<string, Counter<long>> CounterCache =
        new(StringComparer.Ordinal);
    static readonly ConcurrentDictionary<string, Histogram<double>> HistogramCache =
        new(StringComparer.Ordinal);

    public static Counter<long> CounterInstrument(string name) =>
        CounterCache.GetOrAdd(name, static n => Meter.CreateCounter<long>(n));

    public static Histogram<double> HistogramInstrument(string name) =>
        HistogramCache.GetOrAdd(name, static n => Meter.CreateHistogram<double>(n));

    public readonly record struct DeclaredCounter(Counter<long> instrument)
    {
        public void Increment(long n = 1) => instrument.Add(n);
    }

    public readonly record struct DeclaredHistogram(Histogram<double> instrument)
    {
        public void Record(double value) => instrument.Record(value);
    }

    public const string NeuronFire = "neuron.fire";
    public const string NeuronHandle = "neuron.handle";
    public const string GatewayGrpc = "gateway.grpc";
    public const string GatewayReply = "gateway.reply";
    public const string NavigatorRoute = "navigator.route";
    public const string CreatorGenerate = "creator.generate";
    public const string LlmChat = "llm.chat";

    public const string TagNeuronType = "neuron.type";
    public const string TagSynapseType = "synapse.type";
    public const string TagSynapseId = "synapse.id";
    public const string TagCorrelation = "synapse.correlation";
    public const string TagCausation = "synapse.causation";
    public const string TagCaller = "synapse.caller";
    public const string TagReceiver = "synapse.receiver";
    public const string TagDomain = "domain";

    public const string GenAiSystem = "gen_ai.system";
    public const string GenAiOperationName = "gen_ai.operation.name";
    public const string GenAiRequestModel = "gen_ai.request.model";
    public const string GenAiInputTokens = "gen_ai.usage.input_tokens";
    public const string GenAiOutputTokens = "gen_ai.usage.output_tokens";

    public static Synapse CaptureTraceContext(Synapse synapse)
    {
        if (synapse is null) return synapse!;
        var activity = Activity.Current;
        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C)
            return synapse;

        return synapse with
        {
            Headers = (synapse.Headers ?? SynapseMetadata.Create()) with
            {
                Traceparent = activity.Id,
                Tracestate = activity.TraceStateString,
            }
        };
    }

    public static Activity? StartLinkedActivity(
        string name, Synapse synapse, ActivityKind kind = ActivityKind.Internal)
    {
        if (synapse is null) return Source.StartActivity(name, kind);
        
        var headers = synapse.Headers;
        var traceparent = headers?.Traceparent;
        var tracestate = headers?.Tracestate;
        
        var activity = !string.IsNullOrEmpty(traceparent)
            && ActivityContext.TryParse(traceparent, tracestate, isRemote: true, out var parent)
            ? Source.StartActivity(name, kind, parent)
            : Source.StartActivity(name, kind);
            
        StampSynapseTags(activity, synapse);
        return activity;
    }

    public static Activity? StartSynapseActivity(string name, Synapse synapse)
    {
        var activity = Source.StartActivity(name);
        StampSynapseTags(activity, synapse);
        return activity;
    }

    static void StampSynapseTags(Activity? activity, Synapse synapse)
    {
        if (activity is null || synapse is null) return;
        activity.SetTag(TagSynapseType, synapse.GetType()?.Name ?? "Unknown");
        var headers = synapse.Headers;
        if (headers is not null)
        {
            try
            {
                var synapseIdObj = headers.SynapseId;
                if (synapseIdObj != default)
                {
                    activity.SetTag(TagSynapseId, synapseIdObj.Value);
                }
            }
            catch {}

            try
            {
                var correlationIdObj = headers.CorrelationId;
                if (correlationIdObj != default)
                {
                    activity.SetTag(TagCorrelation, correlationIdObj.Value);
                }
            }
            catch {}

            try
            {
                var causationIdObj = headers.CausationId;
                if (causationIdObj is not null)
                {
                    activity.SetTag(TagCausation, causationIdObj.Value.Value);
                }
            }
            catch {}

            try
            {
                var receiverType = headers.ReceiverNeuronType ?? "Unknown";
                var receiverIdObj = headers.ReceiverNeuronId;
                var receiverIdStr = receiverIdObj != default ? (receiverIdObj.Value ?? "Unknown") : "Unknown";
                activity.SetTag(TagReceiver, $"{receiverType}/{receiverIdStr}");
            }
            catch {}
        }
    }
}
