using Ino.Core;
using Orleans;

namespace Ino.Core.Hosting;

/// <summary>
/// Canonical handler — exactly one implementation per synapse type across all installed
/// neurons (duplicate = install rejection in Phase 3 via the analyzer + source
/// generator). Used as the runtime dispatch target for ctx.Fire&lt;T&gt;() calls in Phase 2.
///
/// A single grain class can implement INeuron&lt;T&gt; for multiple synapse types.
/// </summary>
public interface INeuron<TSynapse> : IGrainWithStringKey
    where TSynapse : ISynapse
{
    Task<NeuronResult> HandleAsync(
        TSynapse synapse,
        NeuronContext ctx,
        CancellationToken ct);
}
