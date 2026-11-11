using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Analyzers;

// Every id here is listed in AnalyzerReleases.Unshipped.md (RS2008). Attributes are matched by full metadata name only.
internal static class Rules
{
    private const string Category = "DigitalBrain";

    public static readonly DiagnosticDescriptor ModuleShape = new(
        "DB0001", "Module class needs [Module] and a public static ModuleManifest Manifest",
        "'{0}' derives from DigitalBrain.Module and must {1}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SchedulingInApp = new(
        "DB0002", "Scheduling attribute inside an App class",
        "'{0}' is a neuron scheduling attribute and cannot be used in app '{1}'; put it on a [Neuron] class",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OrleansOnIdentity = new(
        "DB0003", "Orleans attribute on a DigitalBrain identity type",
        "'{0}' carries the Orleans attribute '{1}'; Orleans attributes belong to the runtime, not to DigitalBrain types",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    // 04-03 (CORE-06): ids DB0013/DB0014 sit after Phase 8's reserved DB0001..DB0012 set.
    public static readonly DiagnosticDescriptor SignalLoop = new(
        "DB0013", "Signal loop inside a Neuron or App",
        "'{0}' loops over a {1} stream; use an [On] reaction and Context.Synapses.Ensure",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    // The first rule off by default: consumers opt in with `dotnet_diagnostic.DB0040.severity = warning` in .editorconfig.
    public static readonly DiagnosticDescriptor BareInvariant = new(
        "DB0040", "Bare exception for an invariant inside a neuron or app",
        "'{0}' throws {1} with a message; express the invariant as a Rule and call Check",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor SynapseShape = new(
        "DB0041", "Synapse signature outside reaction, view",
        "'{0}' is not a reaction or view synapse: {1}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StateLocality = new(
        "DB0042", "Synapse state declared in another assembly",
        "'{0}' uses state '{1}' declared in another assembly; a synapse's state record is declared beside it",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StatusString = new(
        "DB0043", "Synapse state carries a status-like member",
        "'{0}' carries a status-like member '{1}'; a synapse says no through a gate, not through state",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoOnInSynapseAssembly = new(
        "DB0044", "[On] reaction in a synapse assembly",
        "'{0}' is an [On] reaction in a synapse assembly; write it as a [Synapse] function",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    // The synapse host refuses a build that references the same list (BannedApis), so skipping this analyzer does not get a build in.
    public static readonly DiagnosticDescriptor BannedApi = new(
        "DB0045", "Banned API in a synapse assembly",
        "'{0}' is banned in a synapse assembly (no network, disk, processes, environment, reflection, native code, dynamic or thread pool); reach the outside through a [Slot] capability",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ShareableContract = new(
        "DB0031", "Cross-brain contract interface requires [Shareable]",
        "Contract interface or member '{0}' violates [Shareable] contract rules",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ReentrancyJustification = new(
        "DB0032", "Neuron reentrancy requires explicit justification comment",
        "Neuron class '{0}' is marked [Reentrant] without an explicit design justification comment explaining why concurrent turn interleaving is safe",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CallCycle = new(
        "DB0033", "Synchronous contract call cycle detected",
        "Synchronous contract invocation cycle detected: {0}. Blocking calls across turns will cause actor deadlocks; use asynchronous signals or [OneWayCall].",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public const string SignalType = "DigitalBrain.Signal";
    public const string NeuronType = "DigitalBrain.Neuron";
    public const string OnAttribute = "DigitalBrain.OnAttribute";
    public const string ModuleAttribute = "DigitalBrain.ModuleAttribute";
    public const string AppAttribute = "DigitalBrain.AppAttribute";
    public const string ModuleType = "DigitalBrain.Module";
    public const string AppType = "DigitalBrain.App";
    public const string ManifestType = "DigitalBrain.ModuleManifest";
    public const string SynapseAttribute = "DigitalBrain.SynapseAttribute";
    public const string StateAttribute = "DigitalBrain.StateAttribute";
    public const string SlotAttribute = "DigitalBrain.SlotAttribute";
    public const string CapabilityAttribute = "DigitalBrain.CapabilityAttribute";
    public const string TickType = "DigitalBrain.Time.Tick";

    // Deliberate copy of DigitalBrain.SynapseReasons (the analyzer package cannot reference the core); the facts on both sides assert the same strings.
    public static class SynapseReasons
    {
        public const string GateNeedsSignal = "gate needs a signal";
        public const string AtMostOneSignal = "at most one signal";
        public const string StateRecord = "state record";
        public const string ViewTakesOnlyState = "view takes only the state";
        public const string PublicStatic = "public static";
        public const string UnknownParameter = "not a signal, the state or CancellationToken";
        public const string SignalFitsParameter = "declared signal type must be assignable to the signal parameter";
        public const string SlotContractNeedsCapability = "slot contract needs [Capability]";
        public const string Schedule = "schedule";
        public const string EveryNeedsTick = "every needs a Tick reaction";
        public const string AliasNeedsEnvelope = "alias reaction takes a SignalEnvelope, not a signal type";
        public const string AsksNeedSyncReaction = "Asks<> slot needs a synchronous reaction";
        public const string AsksExcludeCallingSlot = "Asks<> slot cannot share a function with a calling slot";
        public const string AnswerNeedsAnswered = "Answer parameter needs a synchronous reaction to Answered";
    }

    public static readonly ImmutableHashSet<string> IdentityAttributes = ImmutableHashSet.Create(
        "DigitalBrain.NeuronAttribute", "DigitalBrain.SignalAttribute", AppAttribute, ModuleAttribute);

    public static readonly ImmutableHashSet<string> SchedulingAttributes = ImmutableHashSet.Create(
        "DigitalBrain.ReadOnlyCallAttribute", "DigitalBrain.OneWayCallAttribute", "DigitalBrain.InterleavesAttribute",
        "DigitalBrain.InterleavesWhenAttribute", "DigitalBrain.CallTimeoutAttribute", "DigitalBrain.ReentrantAttribute");

    public static string FullName(INamedTypeSymbol? type)
    {
        if (type is null) { return string.Empty; }
        return type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + type.MetadataName : type.MetadataName;
    }

    public static bool DerivesFrom(INamedTypeSymbol type, string fullName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (FullName(current) == fullName) { return true; }
        }
        return false;
    }

    public static bool IsOrDerivesFrom(ITypeSymbol? type, string fullName)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (FullName(current.OriginalDefinition) == fullName) { return true; }
        }
        return false;
    }

    public static bool Has(ISymbol symbol, string attribute)
    {
        foreach (var data in symbol.GetAttributes())
        {
            if (FullName(data.AttributeClass) == attribute) { return true; }
        }
        return false;
    }

    public static Location LocationOf(AttributeData data, ISymbol fallback)
        => data.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallback.Locations[0];

    public static bool IsOrleans(INamedTypeSymbol? attribute)
    {
        var ns = attribute?.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace) { return false; }
        while (!ns.ContainingNamespace.IsGlobalNamespace) { ns = ns.ContainingNamespace; }
        return ns.Name == "Orleans";
    }
}
