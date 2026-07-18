using System.Reflection;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

public sealed class DeveloperInoSource(IContractCatalog catalog) : IInterpretedNeuronSource
{
    public Task<IReadOnlyList<InterpretedNeuronRegistration>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var registrations = new List<InterpretedNeuronRegistration>();

        var files = new[]
        {
            "DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.Specs.CodeReviewer.ino",
            "DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.Specs.FileAndDirectory.ino",
            "DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.GitHub.GitHub.ino",
            "DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.Specs.SoftwareDeveloper.ino"
        };

        foreach (var file in files)
        {
            using var stream = assembly.GetManifestResourceStream(file);
            if (stream is null)
                throw new InvalidOperationException($"Could not find embedded resource '{file}'");

            using var reader = new StreamReader(stream);
            var source = reader.ReadToEnd();

            var compiled = InoCompiler.Compile(source, catalog);
            if (!compiled.Success || compiled.Linked is null)
                throw new InvalidOperationException($"Failed to compile {file}: {string.Join(";", compiled.Diagnostics.Select(d => d.Message))}");

            var linked = compiled.Linked;
            var descriptor = BuildDescriptor(linked.Doc.Fqn, source, linked);

            var handledSignalSubscriptions = linked.Doc.Handlers
                .Select(h => h.Trigger)
                .OfType<BroadcastTrigger>()
                .Select(trigger => trigger.Fqn)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(fqn => fqn, StringComparer.Ordinal)
                .ToArray();

            registrations.Add(new InterpretedNeuronRegistration(descriptor, handledSignalSubscriptions));
        }

        return Task.FromResult<IReadOnlyList<InterpretedNeuronRegistration>>(registrations);
    }

    private static NeuronDescriptor BuildDescriptor(string unitFqn, string source, LinkedNeuron linked)
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
