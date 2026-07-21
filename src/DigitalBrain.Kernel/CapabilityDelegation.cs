using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

[EditorBrowsable(EditorBrowsableState.Never)]
[GenerateSerializer]
[Alias("db.capability-delegation")]
public sealed class CapabilityDelegation
{
    internal CapabilityDelegation(
        Guid identity,
        SynapseDelivery request,
        GrainId delegateSource,
        OwnerId owner)
    {
        Identity = identity;
        Request = request;
        DelegateSource = delegateSource;
        Owner = owner;
    }

    [Id(0)]
    internal Guid Identity { get; }

    [Id(1)]
    internal SynapseDelivery Request { get; }

    [Id(2)]
    internal GrainId DelegateSource { get; }

    [Id(3)]
    internal OwnerId Owner { get; }

    internal bool Matches(CapabilityDelegation other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Identity == other.Identity
            && DelegateSource == other.DelegateSource
            && Owner == other.Owner
            && Request.SynapseId == other.Request.SynapseId
            && Request.CorrelationId == other.Request.CorrelationId
            && Request.CausationId == other.Request.CausationId
            && Request.Caller == other.Request.Caller
            && Request.Sequence == other.Request.Sequence
            && Request.Timestamp == other.Request.Timestamp
            && Request.Synapse is CapabilityRequested expected
            && other.Request.Synapse is CapabilityRequested actual
            && expected == actual;
    }

    internal void RequireMatches(
        GrainId? actualSource,
        GrainId actualTarget,
        MethodInfo? actualMethod)
    {
        if (Request.Synapse is not CapabilityRequested requested
            || actualSource is null
            || DelegateSource != actualSource.Value
            || requested.Target.ToGrainId() != actualTarget
            || actualMethod?.DeclaringType?.FullName != requested.Contract
            || actualMethod.Name != requested.Method
            || Request.Caller.Owner != Owner
            || requested.Target.Owner != Owner
            || GrainOwnership.RequireOwner(actualSource.Value) != Owner)
        {
            throw new NeuronAuthorizationException(
                "The capability delegation does not authorize the actual runner, owner, target, contract, and method.");
        }
    }

}
