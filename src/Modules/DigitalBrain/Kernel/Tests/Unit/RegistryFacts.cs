using DigitalBrain.Core;
using DigitalBrain.Core.Registry;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class RegistryFacts
{
    [Fact]
    public void CompositionContainsOnlySelectedModuleContracts()
    {
        var composition = new BrainCompositionBuilder().WithModule<FirstModule>().Build();
        var record = Assert.Single(composition.NeuronRegistry.All);
        Assert.Equal("test.emitter", record.Id);
        Assert.Equal(typeof(ITestEmitter), record.ContractType);
        Assert.Equal(typeof(FirstModule).FullName, record.ModuleId);
        Assert.True(record.AgentRoutable);
        Assert.Same(record, composition.NeuronRegistry.Find("test.emitter"));
        Assert.Null(composition.NeuronRegistry.Find("test.other"));
    }

    [Fact]
    public void DuplicateIdsAcrossModulesFailDuringComposition()
        => Assert.Throws<InvalidOperationException>(() => new BrainCompositionBuilder()
            .WithModule<FirstModule>().WithModule<DuplicateModule>().Build());

    [Fact]
    public void NonNeuronContractFailsDuringComposition()
        => Assert.Throws<ArgumentException>(() => new BrainCompositionBuilder()
            .WithModule<InvalidModule>().Build());

    [Fact]
    public void RegistryUsesStableIdAcrossBuilds()
    {
        var first = new BrainCompositionBuilder().WithModule<FirstModule>().Build().NeuronRegistry;
        var second = new BrainCompositionBuilder().WithModule<FirstModule>().Build().NeuronRegistry;
        Assert.Equal(first.All.Select(item => item.Id), second.All.Select(item => item.Id));
    }

    [Fact]
    public async Task SiloExposesRegistryForSelectedModules()
    {
        await using var brain = await UnitTest.Create().WithModule<FirstModule>()
            .StartAsync(TestContext.Current.CancellationToken);
        var registry = brain.SiloServices.GetRequiredService<INeuronRegistry>();
        Assert.NotNull(registry.Find("test.emitter"));
        Assert.Null(registry.Find("test.other"));
    }

    public sealed class FirstModule : IModule, INeuronRegistryContributor
    {
        public IReadOnlyList<NeuronDescriptor> Neurons =>
            [new("test.emitter", typeof(ITestEmitter), "Emitter", "Emits test signals", true)];
        public void Configure(ISiloBuilder silo) { }
    }

    public sealed class DuplicateModule : IModule, INeuronRegistryContributor
    {
        public IReadOnlyList<NeuronDescriptor> Neurons =>
            [new("test.emitter", typeof(IOtherEmitter), "Other", "Emits other signals", false)];
        public void Configure(ISiloBuilder silo) { }
    }

    public sealed class InvalidModule : IModule, INeuronRegistryContributor
    {
        public IReadOnlyList<NeuronDescriptor> Neurons =>
            [new("test.invalid", typeof(string), "Invalid", "Not a neuron", true)];
        public void Configure(ISiloBuilder silo) { }
    }
}
