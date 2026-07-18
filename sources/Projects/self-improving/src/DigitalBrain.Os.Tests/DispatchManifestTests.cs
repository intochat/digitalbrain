using System.Reflection;
using DigitalBrain.Os.Infrastructure.Orleans;

namespace DigitalBrain.Os.Tests;

public sealed class MultiHandlerFixtureNeuron :
    DigitalBrain.Protocol.IHandle<DigitalBrain.Protocol.Domain.Events.BundleInstalled>,
    DigitalBrain.Protocol.IHandle<DigitalBrain.Protocol.Domain.Events.InstallBundle>
{
    public Task HandleAsync(DigitalBrain.Protocol.Domain.Events.BundleInstalled synapse, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task HandleAsync(DigitalBrain.Protocol.Domain.Events.InstallBundle synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class DispatchManifestTests
{
    [Fact]
    public void KnownContracts_IncludesEveryIHandle_ForAMultiHandlerNeuron()
    {
        var contracts = ReadKnownContracts(typeof(MultiHandlerFixtureNeuron).Assembly);

        var fixtureName = typeof(MultiHandlerFixtureNeuron).FullName!;
        var bundleInstalled = typeof(DigitalBrain.Protocol.Domain.Events.BundleInstalled).FullName!;
        var installBundle = typeof(DigitalBrain.Protocol.Domain.Events.InstallBundle).FullName!;

        bool Has(string synapseSimpleName) =>
            contracts.Any(c => c.IsHandle
                && c.Neuron.Contains("MultiHandlerFixtureNeuron")
                && c.Synapse.Contains(synapseSimpleName));

        Has("BundleInstalled").ShouldBeTrue(
            $"manifest should map {fixtureName} -> {bundleInstalled}. Contracts for fixture: {DumpFixture(contracts)}");
        Has("InstallBundle").ShouldBeTrue(
            $"manifest should map {fixtureName} -> {installBundle}. Contracts for fixture: {DumpFixture(contracts)}");
    }

    private static string DumpFixture((string Neuron, string Synapse, bool IsHandle)[] contracts) =>
        string.Join(", ", contracts
            .Where(c => c.Neuron.Contains("MultiHandlerFixtureNeuron"))
            .Select(c => $"({c.Synapse}, handle={c.IsHandle})"));

    private static (string Neuron, string Synapse, bool IsHandle)[] ReadKnownContracts(Assembly assembly)
    {
        var manifest = assembly.GetType("DigitalBrain.SourceGen.DispatchManifest");
        manifest.ShouldNotBeNull("DispatchManifest type should be generated into the test assembly by the analyzer");

        var field = manifest!.GetField("KnownContracts", BindingFlags.Public | BindingFlags.Static);
        field.ShouldNotBeNull("DispatchManifest.KnownContracts should exist");

        var raw = (Array)field!.GetValue(null)!;
        var result = new (string, string, bool)[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            var tuple = raw.GetValue(i)!;
            var type = tuple.GetType();
            var neuron = (string)type.GetField("Item1")!.GetValue(tuple)!;
            var synapse = (string)type.GetField("Item2")!.GetValue(tuple)!;
            var isHandle = (bool)type.GetField("Item3")!.GetValue(tuple)!;
            result[i] = (neuron, synapse, isHandle);
        }
        return result;
    }
}

// Directly tests the MergeReflectionHandlers helper so the test fails if the merge logic is removed
// or becomes a no-op — regardless of whether the source-generated manifest happens to be complete.
public sealed class SynapseDispatchUnionTests
{
    [Fact]
    public void MergeReflectionHandlers_AddsHandlersMissingFromManifest()
    {
        // Simulate a manifest that knew about only ONE of the fixture's IHandle<> synapses.
        var partial = new Dictionary<Type, System.Reflection.MethodInfo>();
        var firstSynapse = typeof(DigitalBrain.Protocol.Domain.Events.BundleInstalled);
        partial[firstSynapse] = typeof(DigitalBrain.Protocol.IHandle<DigitalBrain.Protocol.Domain.Events.BundleInstalled>)
            .GetMethod(nameof(DigitalBrain.Protocol.IHandle<DigitalBrain.Protocol.Domain.Events.BundleInstalled>.HandleAsync))!;

        SynapseDispatch.MergeReflectionHandlers(typeof(MultiHandlerFixtureNeuron), partial);

        // After merge the map must contain every IHandle<> the fixture declares.
        var allHandled = typeof(MultiHandlerFixtureNeuron).GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(DigitalBrain.Protocol.IHandle<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToArray();

        foreach (var st in allHandled)
            partial.Keys.ShouldContain(st, $"MergeReflectionHandlers should have added {st.Name} to the map");

        partial.Count.ShouldBeGreaterThanOrEqualTo(2, "merge should have filled in the omitted handler(s)");
    }
}

