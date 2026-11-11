using System.Reflection;
using DigitalBrain.Runtime.Synapses;
using Xunit;

namespace DigitalBrain.Architecture.Tests.Unit;

public sealed class ChallengerVerificationFacts
{
    private static readonly string[] Shipped =
    [
        "DigitalBrain",
        "DigitalBrain.UI",
        "DigitalBrain.Simulation",
        "DigitalBrain.Testing",
        "DigitalBrain.Identity.Local",
        "DigitalBrain.Runtime.Http",
        "DigitalBrain.Runtime.Synapses",
        "DigitalBrain.Capabilities.Compute",
        "DigitalBrain.Capabilities.Connections",
        "DigitalBrain.Capabilities.Http",
        "DigitalBrain.Capabilities.Identity",
        "DigitalBrain.Capabilities.Secrets",
        "DigitalBrain.Capabilities.Storage",
        "DigitalBrain.Capabilities.Tools",
    ];

    [Fact]
    public void Exactly12AuthoringNamespacesAreDefined()
    {
        string[] expected =
        [
            "DigitalBrain",
            "DigitalBrain.Rules",
            "DigitalBrain.Time",
            "DigitalBrain.UI",
            "DigitalBrain.Capabilities",
            "DigitalBrain.Capabilities.Compute",
            "DigitalBrain.Capabilities.Connections",
            "DigitalBrain.Capabilities.Http",
            "DigitalBrain.Capabilities.Identity",
            "DigitalBrain.Capabilities.Secrets",
            "DigitalBrain.Capabilities.Storage",
            "DigitalBrain.Capabilities.Tools",
        ];

        Assert.Equal(12, AllowedApis.AuthoringNamespaces.Length);
        Assert.Equal(expected.OrderBy(x => x), AllowedApis.AuthoringNamespaces.OrderBy(x => x));
    }

    [Theory]
    [InlineData("DigitalBrain")]
    [InlineData("DigitalBrain.Rules")]
    [InlineData("DigitalBrain.Time")]
    [InlineData("DigitalBrain.UI")]
    [InlineData("DigitalBrain.Capabilities")]
    [InlineData("DigitalBrain.Capabilities.Compute")]
    [InlineData("DigitalBrain.Capabilities.Connections")]
    [InlineData("DigitalBrain.Capabilities.Http")]
    [InlineData("DigitalBrain.Capabilities.Identity")]
    [InlineData("DigitalBrain.Capabilities.Secrets")]
    [InlineData("DigitalBrain.Capabilities.Storage")]
    [InlineData("DigitalBrain.Capabilities.Tools")]
    public void AllowedApis_AcceptsAll12AuthoringNamespaces(string ns)
    {
        var dummyType = $"{ns}.SampleRecord";
        Assert.True(AllowedApis.IsAllowedType(ns, dummyType), $"AllowedApis.IsAllowedType failed for authoring namespace {ns}");
    }

    [Theory]
    [InlineData("DigitalBrain.Neurons", "DigitalBrain.Neurons.INeuronSeat")]
    [InlineData("DigitalBrain.Neurons", "DigitalBrain.Neurons.Neuron")]
    [InlineData("DigitalBrain.Neurons", "DigitalBrain.Neurons.INeuronContext")]
    [InlineData("DigitalBrain.Neurons", "DigitalBrain.Neurons.NeuronServices")]
    [InlineData("DigitalBrain.Units", "DigitalBrain.Units.SynapseHost")]
    [InlineData("DigitalBrain.Units", "DigitalBrain.Units.ISlotResolver")]
    [InlineData("DigitalBrain.Kernel", "DigitalBrain.Kernel.ISynapses")]
    [InlineData("DigitalBrain.Kernel", "DigitalBrain.Kernel.ISignalSink")]
    [InlineData("DigitalBrain.Contracts", "DigitalBrain.Contracts.ReadOnlyCallAttribute")]
    [InlineData("DigitalBrain.Operations", "DigitalBrain.Operations.IOperations")]
    [InlineData("DigitalBrain.Confirmation", "DigitalBrain.Confirmation.ConfirmationPlan")]
    [InlineData("DigitalBrain.Flows", "DigitalBrain.Flows.Flow")]
    [InlineData("DigitalBrain.Scheduling", "DigitalBrain.Scheduling.TimeNeuron")]
    [InlineData("DigitalBrain.Brains", "DigitalBrain.Brains.NeuronAddress")]
    [InlineData("DigitalBrain.Limits", "DigitalBrain.Limits.Policy")]
    [InlineData("DigitalBrain.Modules", "DigitalBrain.Modules.Manifest")]
    [InlineData("DigitalBrain.Access", "DigitalBrain.Access.Caller")]
    [InlineData("DigitalBrain.Identity", "DigitalBrain.Identity.CompoundGrainKey")]
    [InlineData("DigitalBrain.Screens", "DigitalBrain.Screens.SpecComment")]
    [InlineData("DigitalBrain.Hosting", "DigitalBrain.Hosting.IInProcessHostFactory")]
    [InlineData("DigitalBrain.Runtime.Synapses", "DigitalBrain.Runtime.Synapses.SynapseHost")]
    [InlineData("DigitalBrain.Runtime.Orleans.Hosting", "DigitalBrain.Runtime.Orleans.Hosting.HostNeuronContext")]
    public void AllowedApis_RejectsNonAuthoringDigitalBrainNamespaces(string ns, string type)
    {
        Assert.False(AllowedApis.IsAllowedType(ns, type), $"AllowedApis.IsAllowedType unexpectedly accepted closed type {type} in namespace {ns}");
    }

    [Theory]
    [InlineData("DigitalBrain.Rules.Internal", "DigitalBrain.Rules.Internal.Foo")]
    [InlineData("DigitalBrain.Capabilities.Unknown", "DigitalBrain.Capabilities.Unknown.Foo")]
    [InlineData("DigitalBrainX", "DigitalBrainX.Foo")]
    [InlineData("digitalbrain", "digitalbrain.Foo")]
    [InlineData("DigitalBrain.UI.Sub", "DigitalBrain.UI.Sub.Foo")]
    public void AllowedApis_RejectsSubNamespaces_And_CaseMismatches(string ns, string type)
    {
        Assert.False(AllowedApis.IsAllowedType(ns, type), $"AllowedApis.IsAllowedType unexpectedly accepted namespace {ns}");
    }

    [Fact]
    public void AllShippedExportedTypesInAuthoringNamespaces_AreAllowed()
    {
        var openNamespaces = AllowedApis.AuthoringNamespaces.ToHashSet(StringComparer.Ordinal);
        var types = Shipped
            .Select(name => Assembly.Load(name))
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(t => t.Namespace != null && openNamespaces.Contains(t.Namespace))
            .ToList();

        Assert.NotEmpty(types);

        var rejected = types
            .Where(t => !AllowedApis.IsAllowedType(t.Namespace!, t.FullName!))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(rejected);
    }

    [Fact]
    public void AllShippedExportedTypesInNonAuthoringDigitalBrainNamespaces_AreRejected()
    {
        var openNamespaces = AllowedApis.AuthoringNamespaces.ToHashSet(StringComparer.Ordinal);
        var closedTypes = Shipped
            .Select(name => Assembly.Load(name))
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(t => t.Namespace != null && t.Namespace.StartsWith("DigitalBrain", StringComparison.Ordinal) && !openNamespaces.Contains(t.Namespace))
            .ToList();

        Assert.NotEmpty(closedTypes);

        var unexpectedlyAllowed = closedTypes
            .Where(t => AllowedApis.IsAllowedType(t.Namespace!, t.FullName!))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(unexpectedlyAllowed);
    }

    [Fact]
    public void ContextMap_IsStrictlyAcyclic_NoCyclesInDAG()
    {
        var graph = new Dictionary<string, string[]>
        {
            ["Authoring"] = [],
            ["Kernel"] = ["Authoring"],
            ["Neurons"] = ["Authoring", "Kernel"],
            ["Connections"] = ["Authoring"],
            ["Work"] = ["Authoring", "Kernel", "Neurons"],
            ["Limits"] = ["Authoring", "Kernel", "Neurons"],
            ["Units"] = ["Authoring", "Kernel", "Neurons", "Connections"],
            ["Modules"] = ["Authoring", "Limits", "Connections"],
            ["Presentation"] = ["Authoring", "Kernel", "Neurons", "Modules"],
            ["Simulation"] = ["Authoring", "Kernel", "Neurons"],
        };

        var inDegree = graph.Keys.ToDictionary(k => k, _ => 0);
        foreach (var edges in graph.Values)
        {
            foreach (var target in edges)
            {
                inDegree[target]++;
            }
        }

        var queue = new Queue<string>(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        var visited = 0;

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            visited++;

            foreach (var target in graph[node])
            {
                inDegree[target]--;
                if (inDegree[target] == 0)
                {
                    queue.Enqueue(target);
                }
            }
        }

        Assert.Equal(graph.Count, visited);
    }

    [Fact]
    public void ContextMap_RejectsUnauthorizedReverseDependency()
    {
        var map = new Dictionary<string, (string[] Namespaces, string[] MayUse)>
        {
            ["Authoring"] = (["DigitalBrain"], []),
            ["Kernel"] = (["DigitalBrain.Kernel"], ["Authoring"]),
            ["Neurons"] = (["DigitalBrain.Neurons"], ["Authoring", "Kernel"]),
        };

        Assert.False(map["Authoring"].MayUse.Contains("Kernel", StringComparer.Ordinal));
        Assert.False(map["Authoring"].MayUse.Contains("Neurons", StringComparer.Ordinal));
        Assert.False(map["Kernel"].MayUse.Contains("Neurons", StringComparer.Ordinal));
    }

    [Fact]
    public void AuditBridges_AllListedBridgesAreGenuinelyObserved()
    {
        var openNamespaces = AllowedApis.AuthoringNamespaces.ToHashSet(StringComparer.Ordinal);
        bool Open(string? ns) => ns is not null && openNamespaces.Contains(ns);
        bool Closed(string? ns) => ns is not null && ns.StartsWith("DigitalBrain", StringComparison.Ordinal) && !Open(ns);

        var actualEdges = Shipped
            .Select(name => Assembly.Load(name))
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => Open(type.Namespace))
            .SelectMany(type => Signatures.Of(type, visibleOnly: true)
                .Where(mentioned => Closed(mentioned.Namespace))
                .Select(mentioned => $"{type.FullName} -> {mentioned.FullName}"))
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);

        var bridgesField = typeof(AuthoringSurfaceFacts).GetField("Bridges", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(bridgesField);
        var bridges = (HashSet<string>)bridgesField.GetValue(null)!;

        var unlistedLeaks = actualEdges.Except(bridges).ToList();
        var staleDebts = bridges.Except(actualEdges).ToList();

        Assert.Empty(unlistedLeaks);
        Assert.Empty(staleDebts);
    }

    [Fact]
    public void AuditContextMapKnown_NoUnlistedViolations_And_DebtsExplained()
    {
        var knownField = typeof(ContextMapFacts).GetField("Known", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(knownField);
        var known = (HashSet<string>)knownField.GetValue(null)!;

        var mapField = typeof(ContextMapFacts).GetField("Map", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(mapField);
        var mapArray = (Array)mapField.GetValue(null)!;

        var owner = new Dictionary<string, (string Name, string[] MayUse)>(StringComparer.Ordinal);
        foreach (var ctx in mapArray)
        {
            var ctxType = ctx.GetType();
            var name = (string)ctxType.GetProperty("Name")!.GetValue(ctx)!;
            var namespaces = (string[])ctxType.GetProperty("Namespaces")!.GetValue(ctx)!;
            var mayUse = (string[])ctxType.GetProperty("MayUse")!.GetValue(ctx)!;
            foreach (var ns in namespaces)
            {
                owner[ns] = (name, mayUse);
            }
        }

        var signalAssembly = typeof(Signal).Assembly;
        var ours = signalAssembly.GetTypes()
            .Where(type => type.Namespace is { } ns && ns.StartsWith("DigitalBrain", StringComparison.Ordinal));

        var actualAgainst = ours
            .Where(type => owner.ContainsKey(type.Namespace!))
            .SelectMany(type => Signatures.Of(type, visibleOnly: false).Select(mentioned => (type, mentioned)))
            .Where(pair => pair.mentioned.Assembly == signalAssembly && pair.mentioned.Namespace is { } ns && owner.ContainsKey(ns))
            .Where(pair => owner[pair.type.Namespace!] is var from && owner[pair.mentioned.Namespace!] is var to
                && from.Name != to.Name && !from.MayUse.Contains(to.Name, StringComparer.Ordinal))
            .Select(pair => $"{pair.type.FullName} -> {pair.mentioned.FullName}")
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);

        // In any configuration (Debug or Release), there must be ZERO unlisted violations
        var unlistedViolations = actualAgainst.Except(known).ToList();
        Assert.Empty(unlistedViolations);

        // Any Known entries not present in Release must only be compiler-generated state machines (+<)
        var unobservedDebts = known.Except(actualAgainst).ToList();
        var nonCompilerUnobserved = unobservedDebts.Where(d => !d.Contains("+<")).ToList();
        Assert.Empty(nonCompilerUnobserved);
    }
}
