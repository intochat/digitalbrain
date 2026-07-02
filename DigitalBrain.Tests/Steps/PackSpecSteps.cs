using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using DigitalBrain.Tests.Specs;
using Reqnroll;

namespace DigitalBrain.Tests.Steps;

// Reqnroll owns construction of [Binding] classes via its own per-scenario DI container, so this
// class can't rely on xUnit to drive NeuronTestBase's IAsyncLifetime the way NeuronTestBase's own
// [Fact]-based consumers (e.g. PackSpecDriverTests) do. Reqnroll instantiates a binding class once
// per scenario and reuses that SAME instance for every hook and step method resolved within the
// scenario ("Step Definition Methods Rules": instance methods' containing classes are instantiated
// once per scenario), so explicit [BeforeScenario]/[AfterScenario] hooks on this class forward into
// NeuronTestBase.InitializeAsync()/DisposeAsync() and the rest of the scenario's steps observe the
// same initialized cluster through the same HostAdapter seam PackSpecDriverTests uses.
[Binding]
public sealed class PackSpecSteps : NeuronTestBase
{
    private PackSpecDriver? _driver;
    private PackSpecDriver Driver => _driver ??= new PackSpecDriver(new HostAdapter(this));

    [BeforeScenario]
    public Task BeforeScenarioAsync() => InitializeAsync();

    [AfterScenario]
    public Task AfterScenarioAsync() => DisposeAsync();

    [Given(@"a pack ""(.*)"" version ""(.*)"" with source from ""(.*)""")]
    public async Task GivenAPackWithSourceFrom(string name, string version, string sourceFileName)
    {
        var code = sourceFileName switch
        {
            "DriverProbePack.cs" => Authoring.DriverProbePack.Source.Code,
            _ => throw new NotSupportedException($"Unknown pack source file '{sourceFileName}'.")
        };
        await Driver.PublishPackAsync(name, version, code);
    }

    [Given(@"pack ""(.*)"" is installed")]
    public async Task GivenPackIsInstalled(string name) => await Driver.InstallPackAsync(name, "1.0");

    [When(@"I fire synapse ""ExperienceUsed"" at pack ""(.*)"" with pack ""(.*)"" action ""(.*)""")]
    public async Task WhenIFireExperienceUsed(string targetPack, string packArg, string action) =>
        await Driver.FireSynapseAtPackAsync(targetPack, new ExperienceUsed(packArg, action));

    [Then(@"pack ""(.*)"" emits ""(.*)""")]
    public async Task ThenPackEmits(string name, string expectedOutput) =>
        await Driver.AssertEmittedAsync(name, expectedOutput);

    private sealed class HostAdapter(PackSpecSteps owner) : INeuronTestHost
    {
        public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey => owner.Grain<TGrain>(key);
        public Task FireAsync<T>(T synapse) where T : Synapse => owner.FireAsync(synapse);
    }
}
