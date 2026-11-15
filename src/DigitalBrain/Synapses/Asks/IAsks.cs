using System.Text.Json;
using DigitalBrain.Contracts;

namespace DigitalBrain.Units;

[NeuronAlias(AskRules.AsksNeuronType)]
internal interface IAsks
{
    /// <summary>Holds a synapse on the unit's state neuron for Asked. Idempotent.</summary>
    Task Attach(CancellationToken ct);

    /// <summary>Drops every open ask without an answer, the batch stores, the synapse, the timer and the reminder.</summary>
    Task Detach(CancellationToken ct);

    /// <summary>One attempt ended. Ignored unless (batch, index, attempt) is in flight.</summary>
    [OneWayCall]
    Task Settled(AskSettlement settlement);

    /// <summary>Renews the durable execution lease of an in-flight ask attempt.</summary>
    [OneWayCall]
    Task Renew(AskLeaseRenewal renewal);

    /// <summary>Reports ephemeral progress for an in-flight ask attempt without writing to durable storage.</summary>
    [OneWayCall]
    Task ReportProgress(AskProgressReport report);

    /// <summary>Does nothing by itself: the call activates the neuron, and an activation catches up on its unit's <c>unit.asked</c> signals from its cursor.</summary>
    [OneWayCall]
    Task Wake();
}

/// <summary>Ephemeral lease keepalive packet sent by <see cref="AskBatchNeuron"/> to the unit's asks neuron.</summary>
internal sealed record AskLeaseRenewal(
    string Batch,
    int Index,
    int Attempt,
    TimeSpan? Extension = null);

/// <summary>Ephemeral progress packet reported by <see cref="AskBatchNeuron"/> to the unit's asks neuron.</summary>
internal sealed record AskProgressReport(
    string Batch,
    int Index,
    int Attempt,
    string RunId,
    string Key,
    string Status,
    double? Fraction = null,
    TimeSpan? LeaseExtension = null);

/// <summary>The outcome of one attempt reported by <see cref="AskBatchNeuron"/> to the unit's asks neuron.</summary>
/// <param name="Batch">The batch id within the unit.</param>
/// <param name="Index">The zero-based ask index within the batch.</param>
/// <param name="Attempt">The one-based attempt number.</param>
/// <param name="Slot">The slot name from the ask.</param>
/// <param name="Operation">The contract operation name from the ask.</param>
/// <param name="Key">The correlation key from the ask.</param>
/// <param name="Repeatable">Whether the ask is marked repeatable.</param>
/// <param name="Status">How the attempt ended.</param>
/// <param name="Result">The capability's JSON result when <paramref name="Status"/> is <see cref="AnswerStatus.Ok"/>; <see cref="AskRules.NoResult"/> otherwise.</param>
/// <param name="Error">The error reason when <paramref name="Status"/> is not <see cref="AnswerStatus.Ok"/>; <see langword="null"/> otherwise.</param>
/// <param name="Transient">True when another attempt could end differently: the capability threw or the attempt timed out.</param>
internal sealed record AskSettlement(
    string Batch,
    int Index,
    int Attempt,
    string Slot,
    string Operation,
    string Key,
    bool Repeatable,
    AnswerStatus Status,
    JsonElement Result,
    string? Error,
    bool Transient,
    string? Detail = null);

[NeuronAlias(AskRules.BatchNeuronType)]
internal interface IAskBatch
{
    /// <summary>Stores the asks of one batch, once. A second call changes nothing.</summary>
    Task Put(string unit, IReadOnlyList<Ask> asks);

    /// <summary>The stored asks, by index; empty when nothing is stored.</summary>
    [ReadOnlyCall]
    Task<IReadOnlyList<Ask>> All();

    /// <summary>Runs ask <paramref name="index"/> in this call's turn, then reports to the unit's asks neuron.</summary>
    [OneWayCall]
    Task Run(int index, int attempt);

    /// <summary>Clears the store.</summary>
    Task Drop();
}
