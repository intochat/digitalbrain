using DigitalBrain.Mcp;
using TimerNeuron = DigitalBrain.Time.Timers.ITimer;

namespace IntoChat.Tests.Unit;

public sealed class NeuronContractFacts
{
    [Fact]
    public void LoadedContractsIncludeTheTimer()
    {
        var contracts = new NeuronContracts(NeuronContracts.Loaded());
        Assert.Contains(typeof(TimerNeuron).FullName, contracts.Names);
        Assert.Equal(typeof(TimerNeuron), contracts.Find(typeof(TimerNeuron).FullName!));
    }

    [Fact]
    public void UnknownContractIsRejected()
    {
        var contracts = new NeuronContracts(NeuronContracts.Loaded());
        var error = Assert.Throws<ArgumentException>(() => contracts.Find("DigitalBrain.Missing.IMissing"));
        Assert.Contains("neurons_list", error.Message);
    }
}
