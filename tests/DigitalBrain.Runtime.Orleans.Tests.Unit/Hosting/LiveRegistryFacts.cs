using DigitalBrain.Runtime.Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Hosting;

[State("live.registry")]
public sealed record LiveRegistryState(int Count)
{
    public LiveRegistryState() : this(0) { }
}

[Signal("live.registry.pinged")]
public sealed record LivePinged : Signal;

[Signal("live.registry.pinged")]
public sealed record LivePingedAgain : Signal;

public sealed class LiveRegistryFacts
{
    [Fact]
    public void RegisterStateIsVisibleToTryGetAndBumpsVersion()
    {
        var registry = new NeuronTypeRegistry(Array.Empty<System.Reflection.Assembly>());
        var before = registry.Version;

        registry.RegisterState("live.registry", typeof(LiveRegistryState));

        Assert.True(registry.TryGet("live.registry", out var neuronClass));
        Assert.Equal(typeof(StateNeuron<LiveRegistryState>), neuronClass);
        Assert.Equal(HostPolicy.Affine, registry.Require("live.registry").Policy);
        Assert.Equal(NeuronTypeRegistry.DefaultStateName, registry.Require("live.registry").StateName);
        Assert.True(registry.Version > before);

        registry.UnregisterState("live.registry");
        Assert.False(registry.TryGet("live.registry", out _));
    }

    [Fact]
    public void ReplaceSwapsTheTypeBehindAnAlias()
    {
        var map = new SignalTypeMap([]);
        map.Register(typeof(LivePinged));

        map.Replace(typeof(LivePingedAgain));

        Assert.True(map.TryResolve("live.registry.pinged", out var type));
        Assert.Equal(typeof(LivePingedAgain), type);
        Assert.True(map.TryAliasOf(typeof(LivePingedAgain), out _));
        Assert.False(map.TryAliasOf(typeof(LivePinged), out _));

        map.Unregister("live.registry.pinged");
        Assert.False(map.TryResolve("live.registry.pinged", out _));
    }

    [Fact]
    public void RegisterWithADifferentTypeStillThrows()
    {
        var map = new SignalTypeMap([]);
        map.Register(typeof(LivePinged));
        map.Register(typeof(LivePinged));

        Assert.Throws<InvalidOperationException>(() => map.Register(typeof(LivePingedAgain)));
    }
}
