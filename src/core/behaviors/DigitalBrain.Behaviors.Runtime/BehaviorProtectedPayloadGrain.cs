using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Behaviors;

internal interface IBehaviorProtectedPayloadGrain : IGrainWithStringKey
{
    Task<ProtectedPayloadReference> StoreAsync(
        NeuronId task,
        AttemptId attempt,
        byte[] plaintext,
        TimeSpan lifetime,
        CancellationToken cancellationToken,
        Guid stableEntryId = default);

    Task<byte[]> LoadAsync(
        NeuronId task,
        AttemptId attempt,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken);
}

[GrainType("behavior-protected-payload")]
internal sealed class BehaviorProtectedPayloadGrain : DurableGrain, IBehaviorProtectedPayloadGrain
{
    private const string StateName = "behaviors.protected-payload";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);

    private readonly DurableProtectedPayloadStore store;

    public BehaviorProtectedPayloadGrain()
    {
        var state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        var protector = ServiceProvider.GetRequiredService<IDurablePayloadProtector>();
        var owner = new OwnerId(this.GetPrimaryKeyString());
        var time = ServiceProvider.GetKeyedService<TimeProvider>(NeuronTime.ServiceKey)
            ?? ServiceProvider.GetService<TimeProvider>()
            ?? TimeProvider.System;
        store = new DurableProtectedPayloadStore(
            state,
            CommitAsync,
            protector,
            owner,
            time);
    }

    public async Task<ProtectedPayloadReference> StoreAsync(
        NeuronId task,
        AttemptId attempt,
        byte[] plaintext,
        TimeSpan lifetime,
        CancellationToken cancellationToken,
        Guid stableEntryId = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(plaintext);

        if (lifetime <= TimeSpan.Zero)
        {
            lifetime = DefaultLifetime;
        }

        var owner = new OwnerId(this.GetPrimaryKeyString());
        return await store.StoreAsync(
            owner,
            task,
            attempt.Value,
            plaintext,
            lifetime,
            cancellationToken,
            stableEntryId);
    }

    public async Task<byte[]> LoadAsync(
        NeuronId task,
        AttemptId attempt,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var owner = new OwnerId(this.GetPrimaryKeyString());
        var plaintext = await store.LoadAsync(
            owner,
            task,
            attempt.Value,
            reference,
            cancellationToken);
        return plaintext.ToArray();
    }

    private async ValueTask CommitAsync()
    {
        await WriteStateAsync();
    }
}
