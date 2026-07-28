using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.SourceGeneration;

public sealed partial class DispatchManifestGenerator
{
    private static readonly DiagnosticDescriptor NeuronMustBePartial = new(
        "DBGEN001",
        "Neuron contracts must be partial",
        "Neuron contract '{0}' must be declared partial so DigitalBrain can generate its identity",
        "DigitalBrain.SourceGeneration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NeuronAliasIsGenerated = new(
        "DBGEN002",
        "Neuron contract aliases are generated",
        "Neuron contract '{0}' must not declare a type-level Alias; DigitalBrain generates its fully-qualified identity",
        "DigitalBrain.SourceGeneration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static NeuronModel? NeuronOf(GeneratorSyntaxContext syntax, CancellationToken cancellationToken)
    {
        if (syntax.Node is not InterfaceDeclarationSyntax declaration
            || syntax.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol contract
            || contract.DeclaredAccessibility != Accessibility.Public)
        {
            return null;
        }

        var fullName = contract.ToDisplayString(FullName);
        if (fullName != NeuronInterface
            && !contract.AllInterfaces.Any(candidate => candidate.ToDisplayString(FullName) == NeuronInterface))
        {
            return null;
        }

        var isPartial = contract.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<InterfaceDeclarationSyntax>()
            .All(candidate => candidate.Modifiers.Any(SyntaxKind.PartialKeyword));
        var hasAlias = contract.GetAttributes()
            .Any(attribute => attribute.AttributeClass?.ToDisplayString(FullName) == AliasAttribute);

        return new NeuronModel(
            fullName,
            contract.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : contract.ContainingNamespace.ToDisplayString(),
            contract.Name,
            declaration.Identifier.GetLocation(),
            isPartial,
            hasAlias);
    }

    private static void EmitNeuron(SourceProductionContext production, NeuronModel neuron)
    {
        var valid = true;

        if (!neuron.IsPartial)
        {
            production.ReportDiagnostic(Diagnostic.Create(NeuronMustBePartial, neuron.Location, neuron.FullName));
            valid = false;
        }

        if (neuron.HasAlias)
        {
            production.ReportDiagnostic(Diagnostic.Create(NeuronAliasIsGenerated, neuron.Location, neuron.FullName));
            valid = false;
        }

        if (!valid)
        {
            return;
        }

        production.AddSource($"{neuron.FullName}.NeuronIdentity.g.cs", NeuronIdentity(neuron));
    }

    private static string NeuronIdentity(NeuronModel neuron)
    {
        var source = new StringBuilder();

        source.AppendLine("#nullable enable");

        if (neuron.Namespace.Length > 0)
        {
            source.AppendLine();
            source.AppendLine($"namespace {neuron.Namespace};");
        }

        source.AppendLine();
        source.AppendLine($"[global::Orleans.Alias(\"{neuron.FullName}\")]");
        source.AppendLine($"public partial interface {neuron.Name};");

        return source.ToString();
    }

    private sealed class NeuronModel(
        string fullName,
        string @namespace,
        string name,
        Location location,
        bool isPartial,
        bool hasAlias)
    {
        public string FullName { get; } = fullName;

        public string Namespace { get; } = @namespace;

        public string Name { get; } = name;

        public Location Location { get; } = location;

        public bool IsPartial { get; } = isPartial;

        public bool HasAlias { get; } = hasAlias;
    }
}
