using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.SourceGeneration;

[Generator]
public sealed partial class DispatchManifestGenerator : IIncrementalGenerator
{
    private const string AliasAttribute = "Orleans.AliasAttribute";
    private const string HandleInterface = "DigitalBrain.Abstractions.IHandle<TSynapse>";
    private const string EmitInterface = "DigitalBrain.Abstractions.IEmit<TSynapse>";
    private const string NeuronInterface = "DigitalBrain.Abstractions.INeuron";
    private const string ModuleInterface = "DigitalBrain.Abstractions.IModule";
    private const string CompiledModuleInterface = "DigitalBrain.Kernel.ICompiledModule";
    private const string SiloBuilder = "Orleans.Hosting.ISiloBuilder";
    private const string DigitalBrainRuntime = "DigitalBrain.Kernel.DigitalBrainRuntime";

    private static readonly SymbolDisplayFormat FullName =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var wirings = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (syntax, _) => Wirings(syntax))
            .Where(static entries => entries.Length > 0)
            .Collect();

        context.RegisterSourceOutput(wirings, static (production, entries) =>
            production.AddSource("DispatchManifest.g.cs", Manifest(entries.SelectMany(entry => entry).ToImmutableArray())));

        var neurons = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InterfaceDeclarationSyntax,
                static (syntax, cancellationToken) => NeuronOf(syntax, cancellationToken))
            .Where(static neuron => neuron is not null);

        context.RegisterSourceOutput(neurons, static (production, neuron) =>
            EmitNeuron(production, neuron!));

        var moduleCapsules = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (syntax, cancellationToken) => ModuleOf(syntax, cancellationToken))
            .Where(static module => module is not null);

        context.RegisterSourceOutput(moduleCapsules, static (production, module) =>
            EmitModule(production, module!));

        var composition = context.CompilationProvider
            .Select(static (compilation, _) => CompositionOf(compilation));

        context.RegisterSourceOutput(composition, static (production, model) =>
        {
            if (model.Emit)
            {
                production.AddSource("DigitalBrainComposition.g.cs", Composition(model));
            }
        });
    }
}
