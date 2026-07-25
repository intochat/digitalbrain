using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.SourceGeneration;

[Generator]
public sealed class DispatchManifestGenerator : IIncrementalGenerator
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

    private static readonly DiagnosticDescriptor ModuleMustBePartial = new(
        "DBGEN003",
        "Module markers must be partial",
        "Module marker '{0}' must be declared partial so DigitalBrain can generate its capsule",
        "DigitalBrain.SourceGeneration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ModuleMustBeTopLevel = new(
        "DBGEN004",
        "Module markers must be top-level",
        "Module marker '{0}' must not be nested",
        "DigitalBrain.SourceGeneration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ModuleMustBeNonGeneric = new(
        "DBGEN005",
        "Module markers must be non-generic",
        "Module marker '{0}' must not be generic",
        "DigitalBrain.SourceGeneration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ModuleNeedsPublicConstructor = new(
        "DBGEN006",
        "Module markers need a public parameterless constructor",
        "Module marker '{0}' must declare or inherit a public parameterless constructor",
        "DigitalBrain.SourceGeneration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

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

    private static NeuronModel? NeuronOf(
        GeneratorSyntaxContext syntax,
        CancellationToken cancellationToken)
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
            production.ReportDiagnostic(Diagnostic.Create(
                NeuronMustBePartial,
                neuron.Location,
                neuron.FullName));
            valid = false;
        }

        if (neuron.HasAlias)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                NeuronAliasIsGenerated,
                neuron.Location,
                neuron.FullName));
            valid = false;
        }

        if (!valid)
        {
            return;
        }

        production.AddSource(
            $"{neuron.FullName}.NeuronIdentity.g.cs",
            NeuronIdentity(neuron));
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

    private static ModuleCapsuleModel? ModuleOf(
        GeneratorSyntaxContext syntax,
        CancellationToken cancellationToken)
    {
        if (syntax.Node is not ClassDeclarationSyntax declaration
            || syntax.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol module
            || module.DeclaredAccessibility != Accessibility.Public
            || module.IsAbstract
            || !module.AllInterfaces.Any(contract => contract.ToDisplayString(FullName) == ModuleInterface))
        {
            return null;
        }

        var isPartial = module.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<ClassDeclarationSyntax>()
            .All(candidate => candidate.Modifiers.Any(SyntaxKind.PartialKeyword));
        var hasPublicParameterlessConstructor = module.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0
            && constructor.DeclaredAccessibility == Accessibility.Public);

        return new ModuleCapsuleModel(
            module.ToDisplayString(FullName),
            module.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : module.ContainingNamespace.ToDisplayString(),
            module.Name,
            declaration.Identifier.GetLocation(),
            isPartial,
            module.ContainingType is not null,
            module.TypeParameters.Length > 0,
            hasPublicParameterlessConstructor,
            module.IsSealed);
    }

    private static void EmitModule(SourceProductionContext production, ModuleCapsuleModel module)
    {
        var valid = true;

        if (!module.IsPartial)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                ModuleMustBePartial,
                module.Location,
                module.FullName));
            valid = false;
        }

        if (module.IsNested)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                ModuleMustBeTopLevel,
                module.Location,
                module.FullName));
            valid = false;
        }

        if (module.IsGeneric)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                ModuleMustBeNonGeneric,
                module.Location,
                module.FullName));
            valid = false;
        }

        if (!module.HasPublicParameterlessConstructor)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                ModuleNeedsPublicConstructor,
                module.Location,
                module.FullName));
            valid = false;
        }

        if (!valid)
        {
            return;
        }

        production.AddSource(
            $"{module.FullName}.CompiledModule.g.cs",
            CompiledModule(module));
    }

    private static string CompiledModule(ModuleCapsuleModel module)
    {
        var source = new StringBuilder();

        source.AppendLine("#nullable enable");

        if (module.Namespace.Length > 0)
        {
            source.AppendLine();
            source.AppendLine($"namespace {module.Namespace};");
        }

        source.AppendLine();
        source.AppendLine($"public {(module.IsSealed ? "sealed " : string.Empty)}partial class {module.Name} : global::DigitalBrain.Kernel.ICompiledModule");
        source.AppendLine("{");
        source.AppendLine("    public static global::DigitalBrain.Abstractions.ModuleId Id { get; } =");
        source.AppendLine($"        new(\"{module.FullName}\");");
        source.AppendLine();
        source.AppendLine("    global::DigitalBrain.Abstractions.ModuleId");
        source.AppendLine("        global::DigitalBrain.Kernel.ICompiledModule.Id => Id;");
        source.AppendLine();
        source.AppendLine("    void global::DigitalBrain.Kernel.ICompiledModule.PrepareSerialization(");
        source.AppendLine("        global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        source.AppendLine("        => ConfigureSerialization(services);");
        source.AppendLine();
        source.AppendLine("    void global::DigitalBrain.Kernel.ICompiledModule.Activate(");
        source.AppendLine("        global::Orleans.Hosting.ISiloBuilder builder)");
        source.AppendLine("    {");
        source.AppendLine("        ConfigureRuntime(builder);");
        source.AppendLine("        global::DigitalBrain.Kernel.DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(");
        source.AppendLine($"            builder, typeof(global::{module.FullName}).Assembly);");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    static partial void ConfigureSerialization(");
        source.AppendLine("        global::Microsoft.Extensions.DependencyInjection.IServiceCollection services);");
        source.AppendLine();
        source.AppendLine("    static partial void ConfigureRuntime(global::Orleans.Hosting.ISiloBuilder builder);");
        source.AppendLine("}");

        return source.ToString();
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

    private sealed class ModuleCapsuleModel(
        string fullName,
        string @namespace,
        string name,
        Location location,
        bool isPartial,
        bool isNested,
        bool isGeneric,
        bool hasPublicParameterlessConstructor,
        bool isSealed)
    {
        public string FullName { get; } = fullName;

        public string Namespace { get; } = @namespace;

        public string Name { get; } = name;

        public Location Location { get; } = location;

        public bool IsPartial { get; } = isPartial;

        public bool IsNested { get; } = isNested;

        public bool IsGeneric { get; } = isGeneric;

        public bool HasPublicParameterlessConstructor { get; } = hasPublicParameterlessConstructor;

        public bool IsSealed { get; } = isSealed;
    }

    private readonly struct Wiring(string neuron, string synapse, bool isHandler)
    {
        public string Neuron { get; } = neuron;

        public string Synapse { get; } = synapse;

        public bool IsHandler { get; } = isHandler;
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

