using DigitalBrain.Runtime.Orleans;
using DigitalBrain.Runtime.Orleans.Azure.Tests.Integration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Framework.Tests.E2E;

public interface IBrainProbeGrain : IGrainWithStringKey
{
    Task<string> Ping();

    Task Remember(int value);

    Task<int> Recall();
}

[GenerateSerializer]
public sealed class BrainProbeState
{
    [Id(0)]
    public int Value { get; set; }
}

/// <summary>A probe neuron keyed by a BrainId; its state lives in the platform store.</summary>
public sealed class BrainProbeGrain([PersistentState("probe", IntoChatOSNames.PlatformGrainStorage)] IPersistentState<BrainProbeState> state)
    : Grain, IBrainProbeGrain
{
    public Task<string> Ping() => Task.FromResult(this.GetPrimaryKeyString());

    public async Task Remember(int value)
    {
        state.State.Value = value;
        await state.WriteStateAsync();
    }

    public Task<int> Recall() => Task.FromResult(state.State.Value);
}

public sealed class BrainActivationFacts(AzuriteFixture azurite)
{
    public static TheoryData<string> Targets => FrameworkTarget.Names;

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task A_brain_activates_answers_and_keeps_its_platform_state_across_a_restart(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = new BrainId("e2e-" + Guid.NewGuid().ToString("N"));
        await using var host = await ((OrleansFrameworkTarget)FrameworkTarget.For(target, azurite)).Start(ct);

        var probe = host.Services.GetRequiredService<IGrainFactory>().GetGrain<IBrainProbeGrain>(brain.Value);
        Assert.Equal(brain.Value, await probe.Ping());
        await probe.Remember(42);

        await host.Restart(ct);

        var again = host.Services.GetRequiredService<IGrainFactory>().GetGrain<IBrainProbeGrain>(brain.Value);
        Assert.Equal(42, await again.Recall());
    }
}
