using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.SourceGeneration;

public sealed partial class DispatchManifestGenerator
{
    private static ImmutableArray<Wiring> Wirings(GeneratorSyntaxContext syntax)
    {
        if (syntax.SemanticModel.GetDeclaredSymbol(syntax.Node) is not INamedTypeSymbol neuron)
        {
            return ImmutableArray<Wiring>.Empty;
        }

        var declared = ImmutableArray.CreateBuilder<Wiring>();

        foreach (var contract in neuron.AllInterfaces)
        {
            var definition = contract.OriginalDefinition.ToDisplayString();

            if (definition is not (HandleInterface or EmitInterface))
            {
                continue;
            }

            declared.Add(new Wiring(
                neuron.ToDisplayString(FullName),
                contract.TypeArguments[0].ToDisplayString(FullName),
                definition == HandleInterface));
        }

        return declared.ToImmutable();
    }

    private static string Manifest(ImmutableArray<Wiring> wirings)
    {
        var source = new StringBuilder();

        source.AppendLine("#nullable enable");
        source.AppendLine("using System.Diagnostics.CodeAnalysis;");
        source.AppendLine();
        source.AppendLine("namespace DigitalBrain.Generated;");
        source.AppendLine();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine("internal static class DispatchManifest");
        source.AppendLine("{");
        source.AppendLine("    internal static readonly (string Neuron, string Synapse, bool IsHandler)[] Wirings =");
        source.AppendLine("    [");

        foreach (var wiring in wirings.OrderBy(entry => entry.Neuron, System.StringComparer.Ordinal)
            .ThenBy(entry => entry.Synapse, System.StringComparer.Ordinal))
        {
            source.AppendLine($"        (\"{wiring.Neuron}\", \"{wiring.Synapse}\", {(wiring.IsHandler ? "true" : "false")}),");
        }

        source.AppendLine("    ];");
        source.AppendLine("}");

        return source.ToString();
    }

    private readonly struct Wiring(string neuron, string synapse, bool isHandler)
    {
        public string Neuron { get; } = neuron;

        public string Synapse { get; } = synapse;

        public bool IsHandler { get; } = isHandler;
    }
}
