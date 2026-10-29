using System.Reflection;
using System.Reflection.Emit;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Spi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Streams;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

/// <summary>A service the silo container holds; hosted classes receive it through their constructor.</summary>
public sealed class Greeting
{
    public string Text { get; } = "hello";
}

/// <summary>A port a module registers for its neurons.</summary>
public interface IGreetingPort
{
    string Name { get; }
}

internal sealed class GreetingPort : IGreetingPort
{
    public string Name => "port";
}

public interface IGreeter
{
    Task<string> Greet();
}

[Neuron("probe.greeter")]
public sealed class GreeterNeuron(Greeting greeting, IGreetingPort port) : Neuron, IGreeter
{
    public Task<string> Greet() => Task.FromResult($"{greeting.Text} {port.Name} {Id}");
}

/// <summary>The module of the hosting probes: its assembly is what UseModuleTopology hands to AddDigitalBrainRuntime.</summary>
[Module("hosting-test")]
public sealed class HostingModule : Module
{
    public static ModuleManifest Manifest { get; } = new([], [], [], [], []);
}

// 06 D-01, D-13: the module-wide registry, DI construction and the one composition entry.
public sealed class HostActivationFacts
{
    [Fact]
    public void The_registry_maps_type_contract_state_and_policy_across_module_assemblies_and_a_duplicate_type_fails_naming_both()
    {
        var emitted = Emit(("Emitted.Lone", typeof(Neuron), "emitted.lone"));
        var registry = new NeuronTypeRegistry([typeof(ProbeNeuron).Assembly, emitted]);

        Assert.True(registry.TryGet("probe", out var probe));
        Assert.Equal(typeof(ProbeNeuron), probe);
        Assert.True(registry.TryGet("emitted.lone", out var lone));
        Assert.Equal("Emitted.Lone", lone.FullName);
        Assert.True(registry.TryGetByContract(typeof(IProbe), out var byContract));
        Assert.Equal(typeof(ProbeNeuron), byContract.Class);
        Assert.Equal("state", registry.Require("probe").StateName);
        Assert.Equal("count", registry.Require("probe.counter").StateName);
        Assert.Equal(HostPolicy.Stateless, registry.Require("probe.stateless").Policy);
        Assert.Equal(HostPolicy.Affine, registry.Require("probe").Policy);
        Assert.DoesNotContain(registry.Require("probe").Contracts, face => face.Assembly == typeof(Neuron).Assembly);
        Assert.Contains("emitted.missing", Assert.Throws<DigitalBrain.Invocation.BindingException>(() => registry.Require("emitted.missing")).Message);

        var clash = Emit(("Emitted.Clash", typeof(Neuron), "probe"));
        var error = Assert.Throws<InvalidOperationException>(() => new NeuronTypeRegistry([typeof(ProbeNeuron).Assembly, clash]));
        Assert.Contains(typeof(ProbeNeuron).FullName!, error.Message);
        Assert.Contains("Emitted.Clash", error.Message);
    }

    [Fact]
    public void A_neuron_marked_class_deriving_App_fails_startup()
    {
        var app = Emit(("Emitted.ScriptApp", typeof(App), "emitted.app"));
        var error = Assert.Throws<InvalidOperationException>(() => new NeuronTypeRegistry([app]));
        Assert.Contains("script apps run in the sandbox, not in the silo", error.Message);
        Assert.Contains("Emitted.ScriptApp", error.Message);
    }

    [Fact]
    public async Task A_hosted_class_receives_constructor_injected_services_from_the_silo_container()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await new LocalSiloHostFactory(new Services()).Start(new InProcessHostOptions { Modules = [typeof(HostingModule)] }, ct);
        var greeter = NeuronHosting.Get<IGreeter>(host.Services, NeuronId.Parse("b1/probe.greeter/a"));
        Assert.Equal("hello port b1/probe.greeter/a", await greeter.Greet());
    }

    [Fact]
    public void AddDigitalBrainRuntime_runs_once_from_ConfigureTopologySilo_registers_the_runtime_and_relies_on_the_signal_streams_binding()
    {
        using var home = new TempHome();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [IntoChatOSNames.TopologyKey] = Topology.Local.ToConfigValue(),
            [IntoChatOSNames.HomeKey] = home.Path,
        });
        builder.UseClusterStore(new BrainStorageRegistration(typeof(HostingModule).Assembly));
        builder.UseModuleTopology([typeof(HostingModule)], []);
        builder.UseOrleans(silo =>
        {
            silo.ConfigureTopologySilo();
            var error = Assert.Throws<InvalidOperationException>(() => silo.AddDigitalBrainRuntime(typeof(HostingModule).Assembly));
            Assert.Contains("already", error.Message);
            Assert.Single(silo.Services, d => d.ServiceType == typeof(IStreamProvider) && Equals(d.ServiceKey, IntoChatOSNames.SignalStreams));
        });
        using var built = builder.Build();
        var services = built.Services;
        Assert.Contains("probe.greeter", services.GetRequiredService<NeuronTypeRegistry>().Names);
        Assert.NotNull(services.GetRequiredService<SignalPublisher>());
        Assert.NotNull(services.GetRequiredService<BrainSignalHistories>());
        Assert.True(services.GetRequiredService<SignalTypeMap>().TryResolve("probe.counted", out _));
        Assert.IsType<RetentionPolicy>(services.GetRequiredService<IRetentionPolicy>());
    }

    private static AssemblyBuilder Emit(params (string Name, Type Parent, string NeuronType)[] types)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("db.emitted." + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("emitted");
        foreach (var (name, parent, neuronType) in types)
        {
            var type = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed, parent);
            type.SetCustomAttribute(new CustomAttributeBuilder(typeof(NeuronAttribute).GetConstructor([typeof(string)])!, [neuronType]));
            type.DefineDefaultConstructor(MethodAttributes.Public);
            _ = type.CreateType();
        }
        return assembly;
    }

    private sealed class Services : IClusterStoreRegistration
    {
        public void Register(ISiloBuilder silo)
        {
            silo.Services.AddSingleton<Greeting>();
            silo.Services.AddSingleton<IGreetingPort, GreetingPort>();
        }
    }
}
