namespace Ino.Core;

/// <summary>
/// Marker interface on every cross-neuron payload record.
/// Used as the generic constraint on INeuron&lt;T&gt;, IReactsTo&lt;T&gt;, and
/// ctx.Fire&lt;T&gt;() so the compiler rejects passing arbitrary types as synapse payloads.
/// </summary>
public interface ISynapse
{
}
