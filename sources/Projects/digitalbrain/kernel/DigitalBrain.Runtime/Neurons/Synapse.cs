namespace DigitalBrain.Runtime.Neurons;

using System;
using Orleans;
using DigitalBrain.Runtime;

[GenerateSerializer]
public enum RoutingMode { PointToPoint, Broadcast }

[GenerateSerializer]
public sealed record SynapseMetadata(
    [property: Id(0)] SynapseId SynapseId,
    [property: Id(1)] CorrelationId CorrelationId,
    [property: Id(2)] CausationId? CausationId,
    [property: Id(3)] NeuronId CallerNeuronId,
    [property: Id(4)] string? CallerNeuronType,
    [property: Id(5)] NeuronId ReceiverNeuronId,
    [property: Id(6)] string ReceiverNeuronType,
    [property: Id(7)] DateTimeOffset Timestamp,
    [property: Id(8)] string? Traceparent = null,
    [property: Id(9)] string? Tracestate = null,
    [property: Id(10)] RoutingMode RoutingMode = RoutingMode.PointToPoint
)
{
    public static SynapseMetadata Create(
        Guid? synapseId = null,
        Guid? correlationId = null,
        Guid? causationId = null,
        Guid? callerNeuronId = null,
        string? callerNeuronType = null,
        Guid? receiverNeuronId = null,
        string? receiverNeuronType = null,
        DateTimeOffset? timestamp = null,
        string? traceparent = null,
        string? tracestate = null,
        RoutingMode? routingMode = null)
    {
        return new SynapseMetadata(
            new SynapseId(synapseId ?? Guid.NewGuid()),
            new CorrelationId(correlationId ?? Guid.NewGuid()),
            causationId == null ? null : new CausationId(causationId.Value),
            new NeuronId((callerNeuronId ?? Guid.Empty).ToString()),
            callerNeuronType ?? "",
            new NeuronId((receiverNeuronId ?? Guid.Empty).ToString()),
            receiverNeuronType ?? "",
            timestamp ?? DateTimeOffset.UtcNow,
            traceparent,
            tracestate,
            routingMode ?? RoutingMode.PointToPoint
        );
    }
}

[GenerateSerializer]
public abstract record Synapse : ISynapse
{
    [Id(0)]
    public SynapseMetadata Headers { get; init; } = CreateDefaultHeaders();

    public Guid SynapseId
    {
        get => Headers?.SynapseId.Value ?? Guid.Empty;
        init => Headers = (Headers ?? CreateDefaultHeaders()) with { SynapseId = value };
    }

    public Guid CorrelationId
    {
        get => Headers?.CorrelationId.Value ?? Guid.Empty;
        init => Headers = (Headers ?? CreateDefaultHeaders()) with { CorrelationId = value };
    }

    public Guid? CausationId
    {
        get => Headers?.CausationId?.Value;
        init => Headers = (Headers ?? CreateDefaultHeaders()) with { CausationId = value == null ? null : new CausationId(value.Value) };
    }

    public Guid CallerNeuronId
    {
        get => StringKeyToGuid(Headers?.CallerNeuronId.Value ?? string.Empty);
        init => Headers = (Headers ?? CreateDefaultHeaders()) with { CallerNeuronId = new NeuronId(value.ToString()) };
    }

    public string? CallerNeuronType
    {
        get => Headers?.CallerNeuronType;
        init => Headers = (Headers ?? CreateDefaultHeaders()) with { CallerNeuronType = value };
    }

    public Guid ReceiverNeuronId
    {
        get => StringKeyToGuid(Headers?.ReceiverNeuronId.Value ?? string.Empty);
        init => Headers = (Headers ?? CreateDefaultHeaders()) with { ReceiverNeuronId = new NeuronId(value.ToString()) };
    }

    public string ReceiverNeuronType
    {
        get => Headers?.ReceiverNeuronType ?? string.Empty;
        init => Headers = (Headers ?? CreateDefaultHeaders()) with { ReceiverNeuronType = value };
    }

    public DateTimeOffset Timestamp
    {
        get => Headers?.Timestamp ?? default;
        init => Headers = (Headers ?? CreateDefaultHeaders()) with { Timestamp = value };
    }

    public string? Traceparent
    {
        get => Headers?.Traceparent;
        init => Headers = (Headers ?? CreateDefaultHeaders()) with { Traceparent = value };
    }

    public string? Tracestate
    {
        get => Headers?.Tracestate;
        init => Headers = (Headers ?? CreateDefaultHeaders()) with { Tracestate = value };
    }

    public RoutingMode RoutingMode
    {
        get => Headers?.RoutingMode ?? RoutingMode.PointToPoint;
        init => Headers = (Headers ?? CreateDefaultHeaders()) with { RoutingMode = value };
    }

    protected Synapse()
    {
        Headers = CreateDefaultHeaders();
    }

    protected Synapse(SynapseMetadata? headers)
    {
        Headers = headers ?? CreateDefaultHeaders();
    }

    public Synapse Stamp(NeuronId callerNeuronId, string callerNeuronType, Synapse? ambient = null)
    {
        var currentHeaders = Headers ?? CreateDefaultHeaders();

        var correlationId = ambient != null && ambient.CorrelationId != default
            ? new CorrelationId(ambient.CorrelationId)
            : (currentHeaders.CorrelationId.Value != default ? currentHeaders.CorrelationId : Neurons.CorrelationId.New());

        var causationId = currentHeaders.CausationId != null 
            ? currentHeaders.CausationId 
            : (ambient != null ? new CausationId(ambient.SynapseId) : null);

        var rxId = (string.IsNullOrEmpty(currentHeaders.ReceiverNeuronId.Value) || currentHeaders.ReceiverNeuronId.Value == Guid.Empty.ToString()) 
            ? (ambient != null ? new NeuronId(ambient.CallerNeuronId.ToString()) : currentHeaders.ReceiverNeuronId)
            : currentHeaders.ReceiverNeuronId;

        var rxType = string.IsNullOrEmpty(currentHeaders.ReceiverNeuronType) 
            ? (ambient?.CallerNeuronType ?? "") 
            : currentHeaders.ReceiverNeuronType;

        var finalCallerId = (string.IsNullOrEmpty(currentHeaders.CallerNeuronId.Value) || currentHeaders.CallerNeuronId.Value == Guid.Empty.ToString())
            ? callerNeuronId
            : currentHeaders.CallerNeuronId;

        var finalCallerType = string.IsNullOrWhiteSpace(currentHeaders.CallerNeuronType)
            ? callerNeuronType
            : currentHeaders.CallerNeuronType;

        return this with
        {
            Headers = currentHeaders with
            {
                CallerNeuronId = finalCallerId,
                CallerNeuronType = finalCallerType,
                Timestamp = currentHeaders.Timestamp == default ? DateTimeOffset.UtcNow : currentHeaders.Timestamp,
                CorrelationId = correlationId,
                CausationId = causationId,
                ReceiverNeuronId = rxId,
                ReceiverNeuronType = rxType
            }
        };
    }

    private static Guid StringKeyToGuid(string key)
    {
        if (string.IsNullOrEmpty(key)) return Guid.Empty;
        if (Guid.TryParse(key, out var parsed)) return parsed;
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(hash[..16]);
    }

    private static SynapseMetadata CreateDefaultHeaders()
    {
        return new SynapseMetadata(
            Neurons.SynapseId.New(),
            Neurons.CorrelationId.New(),
            null,
            new NeuronId(Guid.Empty.ToString()),
            "",
            new NeuronId(Guid.Empty.ToString()),
            "",
            DateTimeOffset.UtcNow,
            null,
            null,
            RoutingMode.PointToPoint
        );
    }
}
