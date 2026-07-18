using DigitalBrain.Runtime.Runtime;
using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.Runtime.Catalog;
using DigitalBrain.Runtime;

namespace DigitalBrain.Kernel.Runtime;

// E-RUN #36. Producer-side bridge from InoLang's linker product to the runtime's
// two registration records:
//   * NeuronDescriptor    — what IInterpretedNeuronGrain.ConfigureAsync consumes
//                            (the grain re-compiles via IPlanCache).
//   * NeuronCatalogEntry  — what NavigatorRouter resolves over (the gateway
//                            branches on IsInterpreted to pick the dispatch
//                            target — interpreted grain vs native stream).
//
// Both must come from the same LinkedNeuron so the catalog union and the
// interpreter cannot disagree about which synapse FQNs the unit handles.
// Carry-over closed (PR #39 → #40 → here): NeuronDescriptor.Incoming[] is no
// longer caller-authored — BuildDescriptor derives it from the linker.
public static class LinkedPortCatalogContributor
{
    public static NeuronDescriptor BuildDescriptor(string unitFqn, string source, LinkedNeuron linked)
    {
        ArgumentNullException.ThrowIfNull(linked);
        ArgumentException.ThrowIfNullOrWhiteSpace(unitFqn);
        ArgumentNullException.ThrowIfNull(source);

        // v5 C2: synapse port direction is a property of *use*, not *kind*.
        // Incoming  = synapse ports referenced by an `on portName:` handler.
        // Outgoing  = synapse ports referenced by an `emit portName(...)` stmt.
        // (Pre-C2 the linker exposed PortKind.Synapse vs PortKind.Signal as
        // the carrier of this distinction; v5 collapsed both into one kind, so
        // the contributor reads the usage sites directly.)
        var triggerPortNames = linked.Doc.Handlers
            .Select(h => h.Trigger)
            .OfType<PortTrigger>()
            .Select(t => t.Port)
            .ToHashSet(StringComparer.Ordinal);

        var emittedPortNames = CollectEmittedPorts(linked.Doc.Handlers);

        var incoming = linked.Ports.Values
            .Where(port => port.Decl.Kind == PortKind.Synapse
                        && triggerPortNames.Contains(port.Decl.Name))
            .Select(port => new IncomingPort(port.Decl.TargetFqn, port.Decl.Name))
            .ToArray();

        var outgoing = linked.Ports.Values
            .Where(port => port.Decl.Kind == PortKind.Synapse
                        && emittedPortNames.Contains(port.Decl.Name))
            .Select(port => port.Decl.TargetFqn)
            .ToArray();

        return new NeuronDescriptor(unitFqn, incoming, outgoing, source);
    }

    static HashSet<string> CollectEmittedPorts(IEnumerable<Handler> handlers)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handler in handlers)
            WalkForEmits(handler.Body, emitted);
        return emitted;
    }

    static void WalkForEmits(IReadOnlyList<Stmt> body, HashSet<string> emitted)
    {
        foreach (var stmt in body)
            switch (stmt)
            {
                case EmitStmt e: emitted.Add(e.Port); break;
                case IfStmt i:
                    WalkForEmits(i.ThenBody, emitted);
                    WalkForEmits(i.ElseBody, emitted);
                    break;
                case ForEachStmt f: WalkForEmits(f.Body, emitted); break;
                case SpeculateStmt s: WalkForEmits(s.Body, emitted); break;
            }
    }

    public static InterpretedNeuronRegistration BuildRegistration(string source, LinkedNeuron linked)
    {
        ArgumentNullException.ThrowIfNull(linked);
        ArgumentNullException.ThrowIfNull(source);

        var descriptor = BuildDescriptor(linked.Doc.Fqn, source, linked);
        var handledSignalSubscriptions = linked.Doc.Handlers
            .Select(handler => handler.Trigger)
            .OfType<BroadcastTrigger>()
            .Select(trigger => trigger.Fqn)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(fqn => fqn, StringComparer.Ordinal)
            .ToArray();

        return new InterpretedNeuronRegistration(descriptor, handledSignalSubscriptions);
    }

    public static NeuronCatalogEntry BuildEntry(NeuronDescriptor descriptor) =>
        BuildEntry(descriptor, linked: null);

    // E-SDK #63. Production-source overload — `IInterpretedNeuronSource`
    // implementations may not hold a LinkedNeuron at the registration moment
    // (e.g., the Creator persists descriptors as ABI records, not as Linker
    // products). They supply the signal-subscription FQNs directly.
    public static NeuronCatalogEntry BuildEntry(
        NeuronDescriptor descriptor,
        IReadOnlyList<string> handledSignalSubscriptions)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handledSignalSubscriptions);

        var entry = BuildEntry(descriptor, linked: null);
        var sorted = handledSignalSubscriptions
            .Distinct(StringComparer.Ordinal)
            .OrderBy(fqn => fqn, StringComparer.Ordinal)
            .ToArray();
        return entry with { HandledSignalSubscriptions = sorted };
    }

    // The `linked` overload is the production path: E-RUN #37 derives
    // HandledSignalSubscriptions from the same LinkedNeuron the descriptor was
    // built from, so the catalog entry and the interpreter cannot disagree
    // about which signal FQNs the neuron subscribes to (same two-sources-of-
    // truth concern as HandledSynapseTypes in #36). The legacy single-arg
    // overload above passes null — entries built without the linker product
    // simply have no signal subscriptions, the same way they wouldn't have any
    // at runtime.
    public static NeuronCatalogEntry BuildEntry(NeuronDescriptor descriptor, LinkedNeuron? linked)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // Ordinal-sort + distinct matches NeuronCatalogScanner.CollectHandledSynapseTypes
        // so the union over native + interpreted entries presents one shape to
        // NavigatorRouter and the Live tab.
        var handled = descriptor.Incoming
            .Select(port => port.Fqn)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(fqn => fqn, StringComparer.Ordinal)
            .ToArray();

        // `on signal(T):` handlers lower to TriggerKey.Broadcast(T) (see
        // Lowering.cs). Reading the doc-side trigger list keeps the source of
        // truth single — the linker product — even when no plan has been
        // lowered yet (early-binding from the registration path).
        var signalSubscriptions = linked is null
            ? Array.Empty<string>()
            : linked.Doc.Handlers
                .Select(h => h.Trigger)
                .OfType<BroadcastTrigger>()
                .Select(trigger => trigger.Fqn)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(fqn => fqn, StringComparer.Ordinal)
                .ToArray();

        string? uiLayoutJson = null;
        if (linked?.Doc?.Ui != null)
        {
            uiLayoutJson = linked.Doc.Ui.SerializeJson();
        }
        else if (!string.IsNullOrEmpty(descriptor.InoLangSource))
        {
            try
            {
                var bag = new InoLang.Diagnostics.DiagnosticBag();
                var tokens = new InoLang.Lexing.Lexer(descriptor.InoLangSource, bag).Lex();
                var doc = new InoLang.Parsing.Parser(tokens, bag).ParseDocument();
                if (doc?.Ui != null)
                {
                    uiLayoutJson = doc.Ui.SerializeJson();
                }
            }
            catch
            {
                // Soft-fail to prevent startup failures on invalid sources
            }
        }

        return new NeuronCatalogEntry(
            Id: new NeuronId(descriptor.Fqn),
            // InoLang neurons have no asset icon today; the Flutter live graph
            // renders a missing-icon placeholder. E-SDK will add a metadata
            // surface for authored icons.
            Icon: "",
            Capabilities: NeuronCapability.None,
            TypeFullName: descriptor.Fqn,
            CapabilityMarkers: Array.Empty<string>(),
            HandledSynapseTypes: handled,
            Domain: NeuronCatalogScanner.InferDomain(descriptor.Fqn),
            IsInterpreted: true)
        {
            HandledSignalSubscriptions = signalSubscriptions,
            UiLayoutJson = uiLayoutJson
        };
    }
}
