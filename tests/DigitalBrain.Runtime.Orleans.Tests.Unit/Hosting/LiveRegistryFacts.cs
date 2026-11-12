using DigitalBrain.Units;
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
    public void StateRegistrationCreatesUnitNeuronRegistration()
    {
        var reg = NeuronTypeRegistry.StateRegistration("live.registry", typeof(LiveRegistryState));

        Assert.NotNull(reg.Class);
        Assert.Equal("UnitNeuron`1", reg.Class.Name);
        Assert.Equal(typeof(LiveRegistryState), reg.Class.GetGenericArguments()[0]);
        Assert.Equal(HostPolicy.Affine, reg.Policy);
        Assert.Equal(NeuronTypeRegistry.DefaultStateName, reg.StateName);
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
