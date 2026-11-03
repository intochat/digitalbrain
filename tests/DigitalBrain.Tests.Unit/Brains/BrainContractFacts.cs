using System.Reflection;
using DigitalBrain.Brains;
using DigitalBrain.Contracts;
using DigitalBrain.Signals;

namespace DigitalBrain.Tests.Unit.Brains;

public sealed class BrainContractFacts
{
    [ConnectionContract]
    private interface ITestConnection;

    [Fact]
    public void BrainKeyIsMainUnderTheBrain()
    {
        Assert.Equal("brain", IBrain.Type);
        Assert.Equal("b/main", IBrain.Key("b"));
        var owner = new Owner("ada-account");
        var state = new BrainState(owner, DateTimeOffset.UnixEpoch);
        Assert.Equal("ada-account", state.Owner.AccountId);
    }

    [Fact]
    public void SignalAliasesAreUnchanged()
    {
        Assert.Equal("brain.created", typeof(Created).GetCustomAttribute<SignalAttribute>()?.Alias);
        Assert.Equal("brain.activated", typeof(Activated).GetCustomAttribute<SignalAttribute>()?.Alias);
        Assert.Equal("kernel.instance-created", typeof(InstanceCreated).GetCustomAttribute<SignalAttribute>()?.Alias);
        Assert.Equal("kernel.instance-retired", typeof(InstanceRetired).GetCustomAttribute<SignalAttribute>()?.Alias);
        Assert.Equal("kernel.type-added", typeof(TypeAdded).GetCustomAttribute<SignalAttribute>()?.Alias);
    }

    [Fact]
    public void PlainNeuronContractsRecognisesConnectionContracts()
    {
        Assert.True(PlainNeuronContracts.Is(typeof(IBrain)));
        Assert.False(PlainNeuronContracts.IsConnection(typeof(IBrain)));
        Assert.Equal("brain", PlainNeuronContracts.TypeOf(typeof(IBrain)));
        Assert.Equal(new NeuronId(new BrainId("b"), "brain", "main"), PlainNeuronContracts.IdOf(typeof(IBrain), "b/main"));

        Assert.True(PlainNeuronContracts.Is(typeof(ITestConnection)));
        Assert.True(PlainNeuronContracts.IsConnection(typeof(ITestConnection)));
        Assert.Null(PlainNeuronContracts.TypeOf(typeof(ITestConnection)));
        Assert.Equal("gmail", PlainNeuronContracts.ConnectorOf("b/connection/gmail/1"));
        Assert.Equal(new NeuronId(new BrainId("b"), "gmail", "connection/gmail/1"), PlainNeuronContracts.IdOf(typeof(ITestConnection), "b/connection/gmail/1"));
    }
}
