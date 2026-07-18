using Ino.Core;
using Orleans;

namespace Ino.Core.Hosting;

/// <summary>
/// Reactive listener — zero or many implementations per synapse type. Used as the
/// runtime dispatch target for ctx.FireBroadcast&lt;T&gt;() calls in Phase 2. Returns Task
/// (not Task&lt;NeuronResult&gt;) because broadcast is fire-and-forget — per-listener
/// failures are logged but do not fail the broadcast.
/// </summary>
public interface IReactsTo<TSynapse> : IGrainWithStringKey
    where TSynapse : ISynapse
{
    Task ReactAsync(
        TSynapse synapse,
        NeuronContext ctx,
        CancellationToken ct);
}
