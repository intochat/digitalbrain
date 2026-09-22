using System.Reflection;
using System.Text.Json;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Coding;

public sealed record ContractCatalogSnapshot(string EnvironmentHash, IReadOnlyList<string> Modules, IReadOnlyList<string> Contracts, string Example, bool Truncated = false);

public sealed class ContractCatalog(IOptions<CodeExecutionOptions> options)
{
    public ContractCatalogSnapshot Read(IReadOnlyList<string> moduleIds)
    {
        if (moduleIds.Count > 32) { throw new ArgumentException("Select at most 32 modules.", nameof(moduleIds)); }
        var settings = options.Value;
        var paths = settings.ReferencePaths.ToList();
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in moduleIds)
        {
            if (!settings.Modules.TryGetValue(module, out var references))
            { throw new ArgumentException($"Module '{module}' is not installed. Installed module IDs: {string.Join(", ", settings.Modules.Keys.Order(StringComparer.Ordinal))}. Retry with those IDs, or an empty modules array to discover all contracts.", nameof(moduleIds)); }
            paths.AddRange(references);
            foreach (var reference in references) { selected.Add(Path.GetFullPath(reference)); }
        }
        var contracts = paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => selected.Count == 0 || selected.Contains(Path.GetFullPath(p)) || Path.GetFileName(p) == "DigitalBrain.Contracts.dll")
            .Select(Assembly.LoadFrom).SelectMany(a => a.GetExportedTypes())
            .Where(t => t.IsInterface && typeof(INeuron).IsAssignableFrom(t) || typeof(Signal).IsAssignableFrom(t)
                || t == typeof(IDigitalBrain) || t == typeof(ISignalSubscription<>))
            .OrderBy(t => t.Namespace?.StartsWith("DigitalBrain.AI", StringComparison.Ordinal) == true ? 1 : 0)
            .ThenBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => t.FullName + " { " + (t.IsInterface
                ? string.Join("; ", t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => TypeName(m.ReturnType) + " " + m.Name + (m.IsGenericMethod ? "<" + string.Join(",", m.GetGenericArguments().Select(TypeName)) + ">" : "") + "(" + string.Join(", ", m.GetParameters().Select(p => TypeName(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? " = default" : ""))) + ")"))
                : string.Join("; ", t.GetConstructors().Select(c => TypeName(t) + "(" + string.Join(", ", c.GetParameters().Select(p => TypeName(p.ParameterType) + " " + p.Name)) + ")")
                    .Concat(t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(p => TypeName(p.PropertyType) + " " + p.Name)))) + " }")
            .ToArray();
        var snapshot = new ContractCatalogSnapshot(ArtifactStore.EnvironmentHash(new CodeValidationService(settings).Environment()), settings.Modules.Keys.Order(StringComparer.Ordinal).ToArray(), contracts,
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Core;
            using DigitalBrain.Contracts;
            // Replace MySignal and IMyNeuron with fully qualified types from Contracts.
            // Keep this top-level entry point: a class alone is not an executable behavior.
            await BehaviorApp.RunAsync<MyBehavior>(args, brain =>
                [SubscriptionRequirement.For<MySignal>(brain.Get<IMyNeuron>("source-id"))]);
            public sealed class MyBehavior(IDigitalBrain brain) : IBehavior
            {
                public async Task RunAsync(CancellationToken cancellation = default)
                {
                    await using var subscription = await brain.SubscribeAsync<MySignal>(
                        brain.Get<IMyNeuron>("source-id"), cancellation);
                    await foreach (var signal in subscription.ReadAllAsync(cancellation))
                    {
                        // Implement the requested effect here, using the installed neuron methods.
                        System.Console.WriteLine(signal);
                    }
                }
            }
            // ReadAllAsync belongs to the subscription, not IDigitalBrain. No callback overload exists.
            // Save separate xUnit tests of the requested logic, not just the behavior's type name.
            // Tests instantiate the public behavior or its pure helpers without running the entry point.
            // Do not add file-app directives or package references; the host supplies assemblies.
            """);
        while (JsonSerializer.Serialize(snapshot).Length > 24000 && snapshot.Contracts.Count > 0)
        { snapshot = snapshot with { Contracts = snapshot.Contracts.Take(snapshot.Contracts.Count - 1).ToArray(), Truncated = true }; }
        return snapshot;
    }

    private static string TypeName(Type type) => type.IsGenericType
        ? type.Name.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">" : type.Name;
}
