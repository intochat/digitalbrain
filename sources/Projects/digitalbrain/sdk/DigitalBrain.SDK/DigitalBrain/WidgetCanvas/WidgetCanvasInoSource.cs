using System.Reflection;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.WidgetCanvas;

// Registers the authored widget-canvas demo neurons (samples/widget-canvas/*.ino,
// embedded here by the SDK .csproj) so the gateway can route SetClock/RemindMe/
// ShowFlight to them and the IntentDispatcher can read their ui: surfaces from
// the catalog. Mirrors DeveloperInoSource / AspireInoSource — the established
// way an SDK ships interpreted neurons without the filesystem watcher.
public sealed class WidgetCanvasInoSource(IContractCatalog catalog) : IInterpretedNeuronSource
{
    static readonly string[] ResourceNames =
    [
        "DigitalBrain.WidgetCanvas.ClockNeuron.ino",
        "DigitalBrain.WidgetCanvas.ReminderNeuron.ino",
        "DigitalBrain.WidgetCanvas.FlightNeuron.ino",
    ];

    public Task<IReadOnlyList<InterpretedNeuronRegistration>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var registrations = new List<InterpretedNeuronRegistration>();

        foreach (var resource in ResourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
                throw new InvalidOperationException($"Could not find embedded resource '{resource}'");

            using var reader = new StreamReader(stream);
            var source = reader.ReadToEnd();

            var compiled = InoCompiler.Compile(source, catalog);
            if (!compiled.Success || compiled.Linked is null)
                throw new InvalidOperationException(
                    $"Failed to compile {resource}: {string.Join("; ", compiled.Diagnostics.Select(d => d.Message))}");

            var linked = compiled.Linked;
            var descriptor = BuildDescriptor(linked.Doc.Fqn, source, linked);

            var handledSignalSubscriptions = linked.Doc.Handlers
                .Select(handler => handler.Trigger)
                .OfType<BroadcastTrigger>()
                .Select(trigger => trigger.Fqn)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(fqn => fqn, StringComparer.Ordinal)
                .ToArray();

            registrations.Add(new InterpretedNeuronRegistration(descriptor, handledSignalSubscriptions));
        }

        return Task.FromResult<IReadOnlyList<InterpretedNeuronRegistration>>(registrations);
    }

    static NeuronDescriptor BuildDescriptor(string unitFqn, string source, LinkedNeuron linked)
    {
        var incoming = linked.Ports.Values
            .Where(port => port.Decl.Kind == PortKind.Synapse)
            .Select(port => new IncomingPort(port.Decl.TargetFqn, port.Decl.Name))
            .ToArray();

        var outgoing = linked.Ports.Values
            .Where(port => port.Decl.Kind == PortKind.Synapse)
            .Select(port => port.Decl.TargetFqn)
            .ToArray();

        return new NeuronDescriptor(unitFqn, incoming, outgoing, source);
    }
}
