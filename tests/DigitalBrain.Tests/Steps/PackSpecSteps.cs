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
    private readonly bool _wantsThreeSilos;
    private PackSpecDriver? _driver;
    private PackSpecDriver Driver => _driver ??= new PackSpecDriver(new HostAdapter(this));
    private string? _pendingSource;
    private IReadOnlyList<string>? _lastViolations;
    private string? _crossSiloBroadcasterKey;

    public PackSpecSteps(ScenarioContext scenarioContext) =>
        _wantsThreeSilos = scenarioContext.ScenarioInfo.Tags.Contains("cluster");

    protected override short InitialSilosCount => (short)(_wantsThreeSilos ? 3 : 1);

    [BeforeScenario("packspec")]
    public Task BeforeScenarioAsync() => InitializeAsync();

    [AfterScenario("packspec")]
    public Task AfterScenarioAsync() => DisposeAsync();

    [Given(@"the cluster has 3 replicas")]
    public void GivenTheClusterHasThreeReplicas()
    {
        // InitialSilosCount was already resolved from the @cluster tag and consumed by
        // NeuronTestBase.InitializeAsync() in the [BeforeScenario("packspec")] hook, before any step
        // ran — this step exists for Gherkin readability, but it does assert against the TestCluster's
        // actual deployed silo count (not just the configured value) so a wiring regression that
        // silently deploys 1 silo instead of 3 fails here rather than passing vacuously.
        Assert.Equal(3, InitialSilosCount);
        Assert.Equal(3, Cluster.Silos.Count);
    }

    [Given(@"a demo neuron is activated on a different silo than pack ""(.*)""")]
    public async Task GivenADemoNeuronIsActivatedOnADifferentSiloThanPack(string packName) =>
        _crossSiloBroadcasterKey = await Driver.ActivateBroadcasterOnDifferentSiloAsync(packName);

    [When(@"the demo neuron broadcasts synapse ""ProbeMessageSynapse"" with text ""(.*)""")]
    public async Task WhenTheDemoNeuronBroadcastsSynapse(string text) =>
        await Driver.BroadcastFromAsync(_crossSiloBroadcasterKey!, new ProbeMessageSynapse(text) with { IsBroadcast = true });

    [Then(@"pack ""(.*)"" observes the broadcast on another silo")]
    public async Task ThenPackObservesTheBroadcastOnAnotherSilo(string packName) =>
        await Driver.AssertBroadcastObservedAsync(packName);

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

    [Given(@"a pack source that calls ""(.*)""")]
    public void GivenAPackSourceThatCalls(string apiCall) => _pendingSource = apiCall switch
    {
        "System.Net.Http.HttpClient" => """
            using System.Net.Http;
            public sealed class NetProbe : DigitalBrain.Core.Distribution.IPackBehavior
            {
                public string Respond(string input) { var c = new HttpClient(); return input; }
            }
            """,
        _ => throw new NotSupportedException($"Unknown probe API '{apiCall}'.")
    };

    [Given(@"a pack source that only uses collections and LINQ")]
    public void GivenAPackSourceUsingCollectionsAndLinq() => _pendingSource = """
        using System.Collections.Generic;
        using System.Linq;
        public sealed class LinqProbe : DigitalBrain.Core.Distribution.IPackBehavior
        {
            public string Respond(string input) => new List<string> { input }.First();
        }
        """;

    [When(@"the pack is compiled")]
    public void WhenThePackIsCompiled() => _lastViolations = Driver.CheckCompilation(_pendingSource!);

    [Then(@"compilation is rejected with violation ""(.*)""")]
    public void ThenCompilationIsRejectedWithViolation(string expectedPrefix) =>
        Assert.Contains(_lastViolations!, v => v.StartsWith(expectedPrefix, StringComparison.Ordinal));

    [Then(@"compilation is accepted")]
    public void ThenCompilationIsAccepted() => Assert.Empty(_lastViolations!);

    private sealed class HostAdapter(PackSpecSteps owner) : INeuronTestHost
    {
        public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey => owner.Grain<TGrain>(key);
        public Task FireAsync<T>(T synapse) where T : Synapse => owner.FireAsync(synapse);
    }
}
