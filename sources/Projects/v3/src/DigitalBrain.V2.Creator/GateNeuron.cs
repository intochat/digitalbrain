using System.Reflection;
using System.Runtime.Loader;
using DigitalBrain.V2.Core.Runtime;
using DigitalBrain.V2.Core.Synapses;
using DigitalBrain.V2.Ino;
using DigitalBrain.V2.Testing;
using Ping.Contracts;
using Xunit;

namespace DigitalBrain.V2.Creator;

public sealed class GateNeuron : Neuron, IGateNeuron
{
    public async Task HandleAsync(GateNeuronCandidate synapse, CancellationToken ct)
    {
        var compile = Compile(synapse.InoSource);
        var outcome = compile switch
        {
            Compiled compiled => await RunGate(compiled),
            CompileErrors errors => new Failed(errors.Diagnostics),
            null => new Failed(["Compile result was empty."])
        };

        switch (outcome)
        {
            case Passed passed:
                State["activated:" + synapse.Capability] = passed.SimulationType;
                await Emit(new NeuronActivated(synapse.Capability, compile.AssemblyName(), passed.SimulationType));
                break;
            case Failed failed when synapse.Attempt < 2:
                await Emit(new GateFailed(synapse.Capability, failed.Diagnostics, synapse.Attempt));
                break;
            case Failed failed:
                await Emit(new NeuronActivationFailed(synapse.Capability, failed.Diagnostics));
                break;
            case null:
                await Emit(new NeuronActivationFailed(synapse.Capability, ["Gate returned no outcome."]));
                break;
        }
    }

    private static CompileResult Compile(string source)
    {
        var program = new InoParser().Parse(source);
        var capsule = new InoTranspiler().Transpile(program);
        var compile = InoCompiler.Compile(capsule,
        [
            typeof(Synapse).Assembly,
            typeof(Neuron).Assembly,
            typeof(Simulation).Assembly,
            typeof(Ping.Contracts.Ping).Assembly,
            typeof(FactAttribute).Assembly,
            typeof(Assert).Assembly
        ]);

        return compile.Success
            ? new Compiled(capsule, compile.AssemblyBytes)
            : new CompileErrors(compile.Diagnostics);
    }

    private static Task<GateOutcome> RunGate(Compiled compiled)
    {
        var flow = ExecutionContext.SuppressFlow();
        try
        {
            return Task.Run(() => RunGateCore(compiled));
        }
        finally
        {
            flow.Dispose();
        }
    }

    private static async Task<GateOutcome> RunGateCore(Compiled compiled)
    {
        var context = new AssemblyLoadContext("DigitalBrain.V2.Creator.Gate." + Guid.NewGuid().ToString("N"), isCollectible: true);
        context.Resolving += ResolveFromCurrentDomain;

        try
        {
            var assembly = Load(compiled.AssemblyBytes, context);
            var simulation = CreateSimulation(assembly, compiled.Capsule.SimulationFullName);
            await simulation.InitializeAsync();

            try
            {
                await RunGeneratedFact(simulation);
                return new Passed(compiled.Capsule.SimulationFullName);
            }
            finally
            {
                await simulation.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            return new Failed([ex.Message]);
        }
        finally
        {
            context.Unload();
        }
    }

    private static Assembly Load(byte[] assemblyBytes, AssemblyLoadContext context)
    {
        using var stream = new MemoryStream(assemblyBytes);
        return context.LoadFromStream(stream);
    }

    private static Assembly? ResolveFromCurrentDomain(AssemblyLoadContext context, AssemblyName name) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, name.Name, StringComparison.Ordinal));

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
}

internal static class CompileResultExtensions
{
    public static string AssemblyName(this CompileResult result) =>
        result switch
        {
            Compiled compiled => compiled.Capsule.AssemblyName,
            CompileErrors => "compile-errors",
            null => "compile-errors"
        };
}
