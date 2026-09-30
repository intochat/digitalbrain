using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Registry;

namespace DigitalBrain.Microsoft.CSharp;

public sealed record CSharpContractModule(string Id, string? Directive);

public sealed record CSharpContractCatalogSnapshot(IReadOnlyList<CSharpContractModule> Modules, IReadOnlyList<string> Contracts, string Example, bool Truncated = false);

// What a script author (human or agent) sees: the registry's neuron catalog, joined with the
// #:project directive each module's contracts need in a file-based app.
public sealed class CSharpContractDiscovery(IDigitalBrain brain, ModuleInventory inventory, CSharpDirectives directives)
{
    private const int ResponseBudget = 24000;

    public const string Example = """
        #:project /brain/src/Modules/Time/DigitalBrain.Modules.Time.Contracts/DigitalBrain.Modules.Time.Contracts.csproj
        // Add one #:project line per module whose contracts you use (copy them from Directive).
        // DigitalBrain.Client is referenced for you; its namespace and DigitalBrain.Contracts are imported.
        using ITimer = DigitalBrain.Time.Timers.ITimer;
        using DigitalBrain.Time.Timers.Signals;

        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        var timer = brain.Get<ITimer>(brain.Setting("TimerId") ?? "tea");
        await foreach (var tick in brain.On<TimerTick>(timer, brain.Stopping))
        {
            Console.WriteLine($"TimerTick {tick.TimerId} {tick.ObservedAt:O}");
        }
        // Top-level statements run once; loop over brain.On<T>() to keep reacting to signals.
        // Or arm the file on a trigger instead: each signal then starts one run that reads it with
        // var tick = brain.Trigger<TimerTick>(); and exits, so nothing runs between signals.
        // Console output is the log. Settings arrive through brain.Setting(name).
        """;

    public async Task<CSharpContractCatalogSnapshot> Read(IReadOnlyList<string> moduleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(moduleIds);
        cancellationToken.ThrowIfCancellationRequested();
        // The registry's catalog is the truth of what exists; its grain key is the module key "registry".
        var types = await brain.Get<IRegistry>(IRegistry.Key).Types();
        var installed = types.GroupBy(type => type.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(module => module.Key, module => module.ToArray(), StringComparer.OrdinalIgnoreCase);
        var unknown = moduleIds.Where(id => !installed.ContainsKey(id)).ToArray();
        if (unknown.Length > 0)
        { throw new ArgumentException($"Unknown module IDs: {string.Join(", ", unknown)}. Installed module IDs: {string.Join(", ", installed.Keys.Order(StringComparer.Ordinal))}."); }
        var modules = installed.OrderBy(module => module.Key, StringComparer.Ordinal)
            .Select(module => new CSharpContractModule(module.Key, Directive(module.Value[0].ModuleId))).ToArray();
        // Discovery with no modules selected lists the module ids only; select ids to read their contracts.
        var contracts = moduleIds.SelectMany(id => installed[id])
            .OrderBy(type => type.Contract, StringComparer.Ordinal)
            .SelectMany(type => (string[])
                [$"{type.Contract} [neuron id: {type.Id}/<key>] — {type.Description} {{ {string.Join("; ", type.Methods)} }}",
                 .. type.Signals ?? []])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var snapshot = new CSharpContractCatalogSnapshot(modules, contracts, Example);
        while (JsonSerializer.Serialize(snapshot).Length > ResponseBudget && contracts.Count > 0)
        {
            contracts.RemoveAt(contracts.Count - 1);
            snapshot = snapshot with { Contracts = [.. contracts], Truncated = true };
        }
        return snapshot;
    }

    private string? Directive(string moduleId)
        => inventory.Types.FirstOrDefault(module => NeuronType.ModuleIdOf(module) == moduleId) is { } module
            ? ModuleInventory.ContractAssembliesOf(module.Assembly)
                .Select(contracts => directives.Directive(contracts.GetName().Name!))
                .FirstOrDefault(directive => directive is not null)
            : null;
}
