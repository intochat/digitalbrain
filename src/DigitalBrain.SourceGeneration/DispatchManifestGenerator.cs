using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.SourceGeneration;

[Generator]
public sealed class DispatchManifestGenerator : IIncrementalGenerator
{
    private const string HandleInterface = "DigitalBrain.Abstractions.IHandle<TSynapse>";
    private const string EmitInterface = "DigitalBrain.Abstractions.IEmit<TSynapse>";
    private const string ModuleInterface = "DigitalBrain.Abstractions.IModule";
    private const string ModuleSerializationMethod = "ConfigureSerialization";
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

        var composition = context.CompilationProvider
            .Select(static (compilation, _) => CompositionOf(compilation));

        context.RegisterSourceOutput(composition, static (production, model) =>
            production.AddSource("DigitalBrainComposition.g.cs", Composition(model)));
    }

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

    private static CompositionModel CompositionOf(Compilation compilation)
    {
        var moduleContract = compilation.GetTypeByMetadataName(ModuleInterface);

        if (moduleContract is null)
        {
            return new CompositionModel([], emitExtension: false);
        }

        var siloBuilder = compilation.GetTypeByMetadataName(SiloBuilder);
        var modules = compilation.SourceModule.ReferencedAssemblySymbols
            .Append(compilation.Assembly)
            .SelectMany(static assembly => TypesIn(assembly.GlobalNamespace))
            .Where(type => type is
            {
                TypeKind: TypeKind.Class,
                IsAbstract: false,
                DeclaredAccessibility: Accessibility.Public,
            })
            .Where(type => type.AllInterfaces.Any(contract =>
                SymbolEqualityComparer.Default.Equals(contract, moduleContract)))
            .Select(type => new ModuleModel(
                type.ToDisplayString(FullName),
                HasSerializationHook(type, siloBuilder)))
            .GroupBy(static module => module.Name, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static module => module.Name, StringComparer.Ordinal)
            .ToImmutableArray();

        var emitExtension = compilation.AssemblyName != "DigitalBrain.Kernel"
            && siloBuilder is not null
            && compilation.GetTypeByMetadataName(DigitalBrainRuntime) is not null;

        return new CompositionModel(modules, emitExtension);
    }

    private static bool HasSerializationHook(INamedTypeSymbol module, INamedTypeSymbol? siloBuilder)
        => siloBuilder is not null
            && module.GetMembers(ModuleSerializationMethod)
                .OfType<IMethodSymbol>()
                .Any(method => method is
                    {
                        IsStatic: true,
                        DeclaredAccessibility: Accessibility.Public,
                        ReturnsVoid: true,
                        Parameters.Length: 1,
                    }
                    && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, siloBuilder));

    private static IEnumerable<INamedTypeSymbol> TypesIn(INamespaceSymbol scope)
    {
        foreach (var type in scope.GetTypeMembers())
        {
            yield return type;

            foreach (var nested in NestedTypesIn(type))
            {
                yield return nested;
            }
        }

        foreach (var child in scope.GetNamespaceMembers())
        {
            foreach (var type in TypesIn(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypesIn(INamedTypeSymbol containing)
    {
        foreach (var nested in containing.GetTypeMembers())
        {
            yield return nested;

            foreach (var descendant in NestedTypesIn(nested))
            {
                yield return descendant;
            }
        }
    }

    private static string Composition(CompositionModel model)
    {
        var source = new StringBuilder();

        source.AppendLine("#nullable enable");
        source.AppendLine("using System.Diagnostics.CodeAnalysis;");
        source.AppendLine();
        source.AppendLine("namespace DigitalBrain.Generated");
        source.AppendLine("{");
        source.AppendLine("    [ExcludeFromCodeCoverage]");
        source.AppendLine("    internal static class ModuleCatalog");
        source.AppendLine("    {");
        source.AppendLine("        internal static readonly string[] Modules =");
        source.AppendLine("        [");

        foreach (var module in model.Modules)
        {
            source.AppendLine($"            \"{module.Name}\",");
        }

        source.AppendLine("        ];");
        source.AppendLine("    }");
        source.AppendLine("}");

        if (!model.EmitExtension)
        {
            return source.ToString();
        }

        source.AppendLine();
        source.AppendLine("namespace DigitalBrain.Kernel");
        source.AppendLine("{");
        source.AppendLine("    [ExcludeFromCodeCoverage]");
        source.AppendLine("    internal static class GeneratedDigitalBrainSiloBuilderExtensions");
        source.AppendLine("    {");
        source.AppendLine("        internal static global::Orleans.Hosting.ISiloBuilder AddDigitalBrain(");
        source.AppendLine("            this global::Orleans.Hosting.ISiloBuilder builder)");
        source.AppendLine("            => AddDigitalBrain(builder, siloLabel: null);");
        source.AppendLine();
        source.AppendLine("        internal static global::Orleans.Hosting.ISiloBuilder AddDigitalBrain(");
        source.AppendLine("            this global::Orleans.Hosting.ISiloBuilder builder,");
        source.AppendLine("            string? siloLabel)");
        source.AppendLine("        {");
        source.AppendLine("            var selectedModules = global::DigitalBrain.Kernel.DigitalBrainRuntime.Add(");
        source.AppendLine("                builder,");
        source.AppendLine("                siloLabel,");
        source.AppendLine("                new string[]");
        source.AppendLine("                {");

        foreach (var module in model.Modules)
        {
            source.AppendLine($"                    \"{module.Name}\",");
        }

        source.AppendLine("                });");

        foreach (var module in model.Modules.Where(static module => module.HasSerializationHook))
        {
            source.AppendLine();
            source.AppendLine($"            global::{module.Name}.{ModuleSerializationMethod}(builder);");
        }

        foreach (var module in model.Modules)
        {
            source.AppendLine();
            source.AppendLine($"            if (selectedModules.Contains(\"{module.Name}\"))");
            source.AppendLine("            {");
            source.AppendLine($"                global::{module.Name}.Configure(builder);");
            source.AppendLine($"                builder.AddBroadcastHandlers(typeof(global::{module.Name}).Assembly);");
            source.AppendLine("            }");
        }

        source.AppendLine();
        source.AppendLine("            return builder;");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }

    private readonly struct Wiring(string neuron, string synapse, bool isHandler)
    {
        public string Neuron { get; } = neuron;

        public string Synapse { get; } = synapse;

        public bool IsHandler { get; } = isHandler;
    }

    private readonly struct ModuleModel(string name, bool hasSerializationHook)
    {
        public string Name { get; } = name;

        public bool HasSerializationHook { get; } = hasSerializationHook;
    }

    private readonly struct CompositionModel(ImmutableArray<ModuleModel> modules, bool emitExtension)
    {
        public ImmutableArray<ModuleModel> Modules { get; } = modules;

        public bool EmitExtension { get; } = emitExtension;
    }
}
