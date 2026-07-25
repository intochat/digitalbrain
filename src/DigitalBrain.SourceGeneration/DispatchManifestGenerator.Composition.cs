using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.SourceGeneration;

public sealed partial class DispatchManifestGenerator
{
    private static CompositionModel CompositionOf(Compilation compilation)
    {
        var moduleContract = compilation.GetTypeByMetadataName(ModuleInterface);
        var compiledModuleContract = compilation.GetTypeByMetadataName(CompiledModuleInterface);

        if (moduleContract is null || compiledModuleContract is null)
        {
            return new CompositionModel([], emit: false);
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
            .Select(type => new ModuleModel(type.ToDisplayString(FullName)))
            .GroupBy(static module => module.Name, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static module => module.Name, StringComparer.Ordinal)
            .ToImmutableArray();

        var definesModule = TypesIn(compilation.Assembly.GlobalNamespace)
            .Any(type => type.AllInterfaces.Any(contract =>
                SymbolEqualityComparer.Default.Equals(contract, moduleContract)));
        var emit = compilation.AssemblyName != "DigitalBrain.Kernel"
            && !definesModule
            && siloBuilder is not null
            && compilation.GetTypeByMetadataName(DigitalBrainRuntime) is not null;

        return new CompositionModel(modules, emit);
    }

    private static IEnumerable<INamedTypeSymbol> TypesIn(INamespaceSymbol scope)
    {
        foreach (var type in scope.GetTypeMembers())
        {
            yield return type;
        }

        foreach (var child in scope.GetNamespaceMembers())
        {
            foreach (var type in TypesIn(child))
            {
                yield return type;
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
        source.AppendLine("    internal static class CompiledModuleCatalog");
        source.AppendLine("    {");
        source.AppendLine("        internal static global::System.Collections.Generic.IReadOnlyList<");
        source.AppendLine("            global::DigitalBrain.Kernel.ICompiledModule> Modules { get; } =");
        source.AppendLine("        [");

        foreach (var module in model.Modules)
        {
            source.AppendLine($"            new global::{module.Name}(),");
        }

        source.AppendLine("        ];");
        source.AppendLine("    }");
        source.AppendLine("}");
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
        source.AppendLine("            global::DigitalBrain.Kernel.DigitalBrainRuntime.Add(");
        source.AppendLine("                builder,");
        source.AppendLine("                siloLabel,");
        source.AppendLine("                global::DigitalBrain.Generated.CompiledModuleCatalog.Modules);");
        source.AppendLine();
        source.AppendLine("            return builder;");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }

    private readonly struct ModuleModel(string name)
    {
        public string Name { get; } = name;
    }

    private readonly struct CompositionModel(
        ImmutableArray<ModuleModel> modules,
        bool emit)
    {
        public ImmutableArray<ModuleModel> Modules { get; } = modules;

        public bool Emit { get; } = emit;
    }
}
