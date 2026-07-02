using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Tests.Specs;

public class PackSpecDriverTests : NeuronTestBase
{
    [Fact]
    public async Task PublishInstallFire_RoundTrips_A_Minimal_Pack()
    {
        const string packCode = """
            public sealed class DriverProbePack : DigitalBrain.Core.IPackBehavior
            {
                public string Respond(string input) => "driver-echo:" + (input ?? string.Empty);
            }
            """;

        var driver = new PackSpecDriver(new NeuronTestHostAdapter(this));
        await driver.PublishPackAsync("DriverProbePack", "1.0", packCode);
        await driver.InstallPackAsync("DriverProbePack", "1.0");
        await driver.FireSynapseAtPackAsync("DriverProbePack", new ExperienceUsed("DriverProbePack", "probe"));

        await driver.AssertEmittedAsync("DriverProbePack", "driver-echo:probe");
    }

    private sealed class NeuronTestHostAdapter(PackSpecDriverTests owner) : INeuronTestHost
    {
        public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey => owner.Grain<TGrain>(key);
        public Task FireAsync<T>(T synapse) where T : Synapse => owner.FireAsync(synapse);
    }
}
