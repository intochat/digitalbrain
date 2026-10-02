using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Contracts;
using DigitalBrain.Mcp;

namespace DigitalBrain.Mcp.Tests.Unit;

public sealed class NeuronContractFacts
{
    [Fact]
    public void TheCatalogExposesNeuronContractsAndRefusesCredentialContracts()
    {
        var catalog = new NeuronContracts([typeof(INeuron).Assembly, typeof(IIntegrationRegistration).Assembly,
            typeof(DigitalBrain.Time.Timers.ITimer).Assembly]);
        Assert.Equal(typeof(DigitalBrain.Time.Timers.ITimer), catalog.Find(typeof(DigitalBrain.Time.Timers.ITimer).FullName!));
        Assert.DoesNotContain(typeof(INeuron).FullName!, catalog.Names);
        Assert.Throws<ArgumentException>(() => catalog.Find(typeof(IIntegrationRegistration).FullName!));
        Assert.Throws<ArgumentException>(() => catalog.Find("Missing.Contract"));
    }

    [Fact]
    public void DeployedContractsIncludeTheModulesShippedBesideTheMcpServer()
    {
        var catalog = new NeuronContracts(NeuronContracts.Deployed());
        Assert.Contains(typeof(DigitalBrain.Time.Timers.ITimer).FullName!, catalog.Names);
        Assert.DoesNotContain(catalog.Names, name => name.StartsWith("DigitalBrain.Platform.", StringComparison.Ordinal));
    }
}
