using System.Reflection;
using DigitalBrain.V2.Core.Runtime;
using DigitalBrain.V2.Core.Synapses;
using DigitalBrain.V2.Ino;
using DigitalBrain.V2.Testing;
using Ping.Contracts;
using Xunit;

namespace DigitalBrain.V2.Ino.Simulations;

public sealed class InoTranspilerSimulation
{
    [Fact]
    public async Task Generated_ping_simulation_passes()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ping.ino"));
        var program = new InoParser().Parse(source);
        var generated = new InoTranspiler().Transpile(program);

        EnsureContains(generated.Source, "IHandle<global::Ping.Contracts.Ping>");
        EnsureContains(generated.Source, "IEmit<global::Ping.Contracts.Pong>");

        var compile = InoCompiler.Compile(generated,
        [
            typeof(Synapse).Assembly,
            typeof(Neuron).Assembly,
            typeof(Simulation).Assembly,
            typeof(Ping.Contracts.Ping).Assembly,
            typeof(FactAttribute).Assembly,
            typeof(Assert).Assembly
        ]);

        if (!compile.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, compile.Diagnostics));
        }

        var assembly = compile.Load();
        var simulation = CreateSimulation(assembly, generated.SimulationFullName);
        await simulation.InitializeAsync();

        try
        {
            await RunGeneratedFact(simulation);
        }
        finally
        {
            await simulation.DisposeAsync();
        }
    }

    private static Simulation CreateSimulation(Assembly assembly, string typeName)
    {
        var type = assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"Missing generated simulation type '{typeName}'.");

        return Activator.CreateInstance(type) as Simulation
            ?? throw new InvalidOperationException($"Generated type '{typeName}' is not a Simulation.");
    }

    private static async Task RunGeneratedFact(Simulation simulation)
    {
        var method = simulation.GetType()
            .GetMethods()
            .Single(method => method.GetCustomAttributes(typeof(FactAttribute), inherit: false).Length > 0);

        try
        {
            if (method.Invoke(simulation, []) is Task task)
            {
                await task;
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void EnsureContains(string source, string expected)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Generated source does not contain '{expected}'.");
        }
    }
}
