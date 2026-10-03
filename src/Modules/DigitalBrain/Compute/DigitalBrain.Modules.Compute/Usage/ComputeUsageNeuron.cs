using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Compute.Usage;

[GrainType("compute.usage")]
internal sealed class ComputeUsageNeuron(IUsageStore store) : Neuron, IComputeUsage
{
    public async Task Append(string workspace, string id, string payload, DateTimeOffset revision, CancellationToken ct = default)
    {
        RequireScope(workspace);
        await store.AppendAsync(this.GetPrimaryKeyString(), workspace, id, payload, ct, revision);
    }
    public async Task<UsagePage> Read(string workspace, int limit, string? cursor, CancellationToken ct = default)
    {
        RequireScope(workspace);
        return await store.ReadAsync(this.GetPrimaryKeyString(), workspace, limit, cursor, ct);
    }

    private void RequireScope(string workspace)
    {
        if (CallerContextStamper.TryGet(out var caller) && caller.Kind != CallerKind.Platform
            && (caller.AccountId != this.GetPrimaryKeyString() || workspace != BrainScope.CurrentId()))
        { throw new UnauthorizedAccessException("Usage belongs to another brain."); }
    }
}
