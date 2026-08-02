using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Behaviors.Runtime;

internal interface IBehaviorProtectedTriggerGrain : IGrainWithStringKey
{
    Task<ProtectedPayloadReference> StoreAsync(
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        byte[] plaintext,
        CancellationToken cancellationToken);

    Task<byte[]> LoadAsync(
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken);
}

[GrainType("behavior-protected-trigger")]
internal sealed class BehaviorProtectedTriggerGrain : DurableGrain, IBehaviorProtectedTriggerGrain
{
    private const string StateName = "behaviors.protected-trigger";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);

    private readonly DurableProtectedTriggerStore store;

    public BehaviorProtectedTriggerGrain()
    {
        var state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        var protector = ServiceProvider.GetRequiredService<IDurablePayloadProtector>();
        var owner = new OwnerId(this.GetPrimaryKeyString());
        store = new DurableProtectedTriggerStore(
            state,
            CommitAsync,
            protector,
            owner,
            TimeProvider.System);
    }

    public async Task<ProtectedPayloadReference> StoreAsync(
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        byte[] plaintext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);

        var owner = new OwnerId(this.GetPrimaryKeyString());
        return await store.StoreAsync(
            owner,
            task,
            behavior,
            revision,
            caseId,
            plaintext,
            DefaultLifetime,
            cancellationToken);
    }

    public async Task<byte[]> LoadAsync(
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);

        var owner = new OwnerId(this.GetPrimaryKeyString());
        var plaintext = await store.LoadAsync(
            owner,
            task,
            behavior,
            revision,
            caseId,
            reference,
            cancellationToken);
        return plaintext.ToArray();
    }

    private async ValueTask CommitAsync()
    {
        await WriteStateAsync();
    }
}
