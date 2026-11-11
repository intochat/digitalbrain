using System.Reflection;
using Xunit;

namespace DigitalBrain.Architecture.Tests.Unit;

/// <summary>
/// The context map of DigitalBrain.dll as data. Every DigitalBrain namespace belongs to one context; a context mentions, in base
/// types and signatures of any visibility, only itself and the contexts listed for it. Method bodies are not read.
/// </summary>
public sealed class ContextMapFacts
{
    private sealed record Context(string Name, string[] Namespaces, string[] MayUse);

    private static readonly Context[] Map =
    [
        new("Authoring", ["DigitalBrain", "DigitalBrain.Rules", "DigitalBrain.Time", "DigitalBrain.Capabilities", "DigitalBrain.Contracts", "DigitalBrain.Access"], []),
        new("Kernel", ["DigitalBrain.Kernel", "DigitalBrain.Invocation", "DigitalBrain.Signals", "DigitalBrain.Brains", "DigitalBrain.Identity"], ["Authoring"]),
        new("Neurons", ["DigitalBrain.Neurons", "DigitalBrain.State", "DigitalBrain.Errors", "DigitalBrain.Log"], ["Authoring", "Kernel"]),
        new("Connections", ["DigitalBrain.Connections"], ["Authoring"]),
        new("Work", ["DigitalBrain.Operations", "DigitalBrain.Confirmation", "DigitalBrain.Flows", "DigitalBrain.Scheduling"], ["Authoring", "Kernel", "Neurons"]),
        new("Limits", ["DigitalBrain.Limits"], ["Authoring", "Kernel", "Neurons"]),
        new("Units", ["DigitalBrain.Units"], ["Authoring", "Kernel", "Neurons", "Connections"]),
        new("Modules", ["DigitalBrain.Modules", "DigitalBrain.Modules.Configuration", "DigitalBrain.Modules.Options", "DigitalBrain.Modules.Validation"], ["Authoring", "Limits", "Connections"]),
        new("Presentation", ["DigitalBrain.Screens", "DigitalBrain.Signals.Watch"], ["Authoring", "Kernel", "Neurons", "Modules"]),
        new("Simulation", ["DigitalBrain.Hosting"], ["Authoring", "Kernel", "Neurons"]),
    ];

    // Mentions against the map that exist during migration, as "Type -> Type". It may only shrink.
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "DigitalBrain.Access.AccessGate -> DigitalBrain.Invocation.BoundCall",
        "DigitalBrain.Kernel.ReactionTable -> DigitalBrain.Neurons.Neuron",
        "DigitalBrain.Kernel.ReactionTable+<Invoke>d__3 -> DigitalBrain.Neurons.Neuron",
        "DigitalBrain.Kernel.ReactionTable+<React>d__2 -> DigitalBrain.Neurons.Neuron",
        "DigitalBrain.Kernel.Reactions -> DigitalBrain.Neurons.Neuron",
        "DigitalBrain.Kernel.Reactions+<Dispatch>d__0 -> DigitalBrain.Neurons.Neuron",
        "DigitalBrain.Kernel.Reactions+<Dispatch>d__0 -> DigitalBrain.Units.ISynapseReceiver",
        "DigitalBrain.Limits.PoliciesNeuron -> DigitalBrain.Confirmation.ConfirmationResolved",
        "DigitalBrain.Limits.PoliciesNeuron+<>c__DisplayClass1_0 -> DigitalBrain.Confirmation.ConfirmationResolved",
        "DigitalBrain.Limits.PoliciesNeuron+<OnResolved>d__1 -> DigitalBrain.Confirmation.ConfirmationResolved",
        "DigitalBrain.Limits.PoliciesNeuron+<OnResolved>d__1 -> DigitalBrain.Confirmation.ConfirmationResult",
        "DigitalBrain.Limits.PoliciesNeuron+<OnResolved>d__1 -> DigitalBrain.Confirmation.IConfirmationRequests",
        "DigitalBrain.IScreen -> DigitalBrain.Units.PublishedView",
        "DigitalBrain.Limits.PoliciesNeuron+<Propose>d__0 -> DigitalBrain.Confirmation.ConfirmationPlan",
        "DigitalBrain.Limits.PoliciesNeuron+<Propose>d__0 -> DigitalBrain.Confirmation.ConfirmationResult",
        "DigitalBrain.Limits.PoliciesNeuron+<Propose>d__0 -> DigitalBrain.Confirmation.IConfirmationRequests",
        "DigitalBrain.NeuronId -> DigitalBrain.Identity.CompoundGrainKey",
        "DigitalBrain.Reaction`1 -> DigitalBrain.Units.Ask",
        "DigitalBrain.Screen`1 -> DigitalBrain.Neurons.IStateInspectable",
        "DigitalBrain.Screen`1 -> DigitalBrain.Neurons.Neuron`1",
        "DigitalBrain.Screen`1 -> DigitalBrain.Units.PublishedView",
        "DigitalBrain.Screen`1+<Activate>d__11 -> DigitalBrain.Units.PublishedView",
        "DigitalBrain.Screen`1+<StoreState>d__15 -> DigitalBrain.Units.PublishedView",
        "DigitalBrain.Screen`1+<StoreState>d__16 -> DigitalBrain.Units.PublishedView",
        "DigitalBrain.StoredState`1 -> DigitalBrain.Units.PublishedView",
        "DigitalBrain.Units.InstallsNeuron -> DigitalBrain.Confirmation.ConfirmationResolved",
        "DigitalBrain.Units.InstallsNeuron+<DigitalBrain-Units-IInstallsCallbacks-Restored>d__8 -> DigitalBrain.Confirmation.ConfirmationResolved",
        "DigitalBrain.Units.InstallsNeuron+<Install>d__4 -> DigitalBrain.Confirmation.ConfirmationPlan",
        "DigitalBrain.Units.InstallsNeuron+<Install>d__4 -> DigitalBrain.Confirmation.ConfirmationResult",
        "DigitalBrain.Units.InstallsNeuron+<Install>d__4 -> DigitalBrain.Confirmation.IConfirmationRequests",
        "DigitalBrain.Units.InstallsNeuron+<Judge>d__9 -> DigitalBrain.Confirmation.ConfirmationResolved",
        "DigitalBrain.Units.InstallsNeuron+<Judge>d__9 -> DigitalBrain.Confirmation.ConfirmationResult",
        "DigitalBrain.Units.InstallsNeuron+<Judge>d__9 -> DigitalBrain.Confirmation.IConfirmationRequests",
        "DigitalBrain.Units.InstallsNeuron+<RestoreThenJudge>d__16 -> DigitalBrain.Confirmation.ConfirmationResolved",
    };

    // Compiler-embedded and generated types live in System.* and Microsoft.* namespaces inside the assembly; they are not ours.
    private static IEnumerable<Type> Ours => typeof(Signal).Assembly.GetTypes()
        .Where(type => type.Namespace is { } ns && ns.StartsWith("DigitalBrain", StringComparison.Ordinal));

    [Fact]
    public void EveryCoreNamespaceBelongsToOneContext()
    {
        var placed = Map.SelectMany(context => context.Namespaces).ToHashSet(StringComparer.Ordinal);
        var unplaced = Ours.Select(type => type.Namespace!).Distinct().Where(ns => !placed.Contains(ns)).Order(StringComparer.Ordinal).ToList();

        Assert.True(unplaced.Count == 0, "Namespaces of DigitalBrain.dll in no context: " + string.Join(", ", unplaced));
    }

    [Fact]
    public void AContextMentionsOnlyItselfAndTheContextsItMayUse()
    {
        var owner = Map.SelectMany(context => context.Namespaces.Select(ns => (ns, context))).ToDictionary(pair => pair.ns, pair => pair.context, StringComparer.Ordinal);
        var against = Ours
            .Where(type => owner.ContainsKey(type.Namespace!))
            .SelectMany(type => Signatures.Of(type, visibleOnly: false).Select(mentioned => (type, mentioned)))
            .Where(pair => pair.mentioned.Assembly == typeof(Signal).Assembly && pair.mentioned.Namespace is { } ns && owner.ContainsKey(ns))
            .Where(pair => owner[pair.type.Namespace!] is var from && owner[pair.mentioned.Namespace!] is var to
                && from != to && !from.MayUse.Contains(to.Name, StringComparer.Ordinal))
            .Select(pair => $"{pair.type.FullName} -> {pair.mentioned.FullName}")
            .Distinct()
            .Where(edge => !Known.Contains(edge))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(against.Count == 0, "Mentions against the context map: " + string.Join(", ", against));
    }
}
