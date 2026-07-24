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
    private const string SynapseBase = "DigitalBrain.Abstractions.Synapse";
    private const string CompiledModuleInterface = "DigitalBrain.Kernel.ICompiledModule";
    private const string DigitalBrainClient = "DigitalBrain.Client.IDigitalBrain";
    private const string TestJournal = "DigitalBrain.Testing.TestJournal";
    private const string TestOwner = "DigitalBrain.Testing.TestOwner";
    private const string ReqnrollBinding = "Reqnroll.BindingAttribute";
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

    private static readonly DiagnosticDescriptor AmbiguousVocabularyName = new(
        "DBGEN007",
        "Gherkin vocabulary short names must be unique",
        "The short {0} name '{1}' is ambiguous; use one of: {2}",
        "DigitalBrain.SourceGeneration",
        DiagnosticSeverity.Info,
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
            if (model.EmitCatalog)
            {
                production.AddSource("DigitalBrainComposition.g.cs", Composition(model));
            }
        });

        var vocabulary = context.CompilationProvider
            .Select(static (compilation, _) => VocabularyOf(compilation));

        context.RegisterSourceOutput(vocabulary, static (production, model) =>
        {
            if (!model.Emit)
            {
                return;
            }

            foreach (var ambiguity in model.Ambiguities)
            {
                production.ReportDiagnostic(Diagnostic.Create(
                    AmbiguousVocabularyName,
                    Location.None,
                    ambiguity.Kind,
                    ambiguity.ShortName,
                    string.Join(", ", ambiguity.Candidates)));
            }

            production.AddSource(
                "GeneratedTestVocabulary.g.cs",
                TestVocabulary(model));
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

    private static VocabularyModel VocabularyOf(Compilation compilation)
    {
        var neuronContract = compilation.GetTypeByMetadataName(NeuronInterface);
        var synapseContract = compilation.GetTypeByMetadataName(SynapseBase);
        var testOwner = compilation.GetTypeByMetadataName(TestOwner);
        var testJournal = compilation.GetTypeByMetadataName(TestJournal);
        var client = compilation.GetTypeByMetadataName(DigitalBrainClient);
        var binding = compilation.GetTypeByMetadataName(ReqnrollBinding);

        if (neuronContract is null
            || synapseContract is null
            || testOwner is null
            || testJournal is null
            || client is null
            || binding is null)
        {
            return new VocabularyModel([], [], [], emit: false);
        }

        var visibleTypes = compilation.SourceModule.ReferencedAssemblySymbols
            .Append(compilation.Assembly)
            .SelectMany(static assembly => TypesIn(assembly.GlobalNamespace))
            .Where(static type => type.DeclaredAccessibility == Accessibility.Public)
            .Where(static type => type.TypeParameters.Length == 0)
            .GroupBy(
                static type => type.ToDisplayString(FullName),
                StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();

        var neurons = visibleTypes
            .Where(type => type.TypeKind == TypeKind.Interface)
            .Where(type => !SymbolEqualityComparer.Default.Equals(
                type,
                neuronContract))
            .Where(type => type.AllInterfaces.Any(contract =>
                SymbolEqualityComparer.Default.Equals(
                    contract,
                    neuronContract)))
            .Select(type => new VocabularyNeuron(
                type.ToDisplayString(FullName),
                type.Name))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToImmutableArray();

        var synapses = visibleTypes
            .Where(type => type.TypeKind is TypeKind.Class
                && !type.IsAbstract)
            .Where(type => InheritsFrom(type, synapseContract))
            .Select(type => new VocabularySynapse(
                type.ToDisplayString(FullName),
                type.Name,
                SynapseFactory(type)))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToImmutableArray();

        var ambiguities = Ambiguities("neuron", neurons
                .Select(static neuron =>
                    (neuron.ShortName, neuron.FullName)))
            .Concat(Ambiguities("synapse", synapses
                .Select(static synapse =>
                    (synapse.ShortName, synapse.FullName))))
            .OrderBy(static ambiguity => ambiguity.Kind, StringComparer.Ordinal)
            .ThenBy(
                static ambiguity => ambiguity.ShortName,
                StringComparer.Ordinal)
            .ToImmutableArray();

        return new VocabularyModel(
            neurons,
            synapses,
            ambiguities,
            emit: true);
    }

    private static ImmutableArray<VocabularyAmbiguity> Ambiguities(
        string kind,
        IEnumerable<(string ShortName, string FullName)> entries)
        => entries
            .GroupBy(
                static entry => entry.ShortName,
                StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(group => new VocabularyAmbiguity(
                kind,
                group.Key,
                group.Select(static entry => entry.FullName)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToImmutableArray()))
            .ToImmutableArray();

    private static bool InheritsFrom(
        INamedTypeSymbol type,
        INamedTypeSymbol expected)
    {
        for (var current = type.BaseType;
            current is not null;
            current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expected))
            {
                return true;
            }
        }

        return false;
    }

    private static string SynapseFactory(INamedTypeSymbol synapse)
    {
        var constructors = synapse.InstanceConstructors
            .Where(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public)
            .Select(constructor => new
            {
                Constructor = constructor,
                Arguments = constructor.Parameters
                    .Select(parameter => ArgumentExpression(
                        parameter.Type,
                        parameter.Name))
                    .ToArray(),
            })
            .Where(static candidate =>
                candidate.Arguments.All(static argument =>
                    argument is not null))
            .OrderByDescending(static candidate =>
                candidate.Constructor.Parameters.Length)
            .ThenBy(
                static candidate =>
                    candidate.Constructor.ToDisplayString(FullName),
                StringComparer.Ordinal)
            .FirstOrDefault();

        if (constructors is null)
        {
            return UnsupportedFactory(synapse);
        }

        var constructorParameterNames = new HashSet<string>(
            constructors.Constructor.Parameters
                .Select(static parameter => parameter.Name),
            StringComparer.OrdinalIgnoreCase);
        var settableProperties = synapse.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(static property => !property.IsStatic
                && property.DeclaredAccessibility == Accessibility.Public
                && property.SetMethod is
                {
                    DeclaredAccessibility: Accessibility.Public,
                    IsInitOnly: false,
                })
            .Where(property =>
                !constructorParameterNames.Contains(property.Name))
            .Select(property => new
            {
                Property = property,
                Expression = ArgumentExpression(
                    property.Type,
                    $"value{property.Name}"),
            })
            .Where(static property => property.Expression is not null)
            .OrderBy(static property => property.Property.Name, StringComparer.Ordinal)
            .ToArray();
        var arguments = string.Join(
            ", ",
            constructors.Arguments.Select(static argument => argument!));
        var constructed =
            $"new global::{synapse.ToDisplayString(FullName)}({arguments})";

        if (settableProperties.Length == 0)
        {
            return $"static arguments => {constructed}";
        }

        var source = new StringBuilder();
        source.AppendLine("static arguments =>");
        source.AppendLine("            {");
        source.AppendLine($"                var synapse = {constructed};");

        foreach (var property in settableProperties)
        {
            var propertyName = StringLiteral(property.Property.Name);
            source.AppendLine(
                $"                if (arguments.TryGetValue(\"{propertyName}\", out var value{property.Property.Name}))");
            source.AppendLine("                {");
            source.AppendLine(
                $"                    synapse.{EscapeIdentifier(property.Property.Name)} = {property.Expression};");
            source.AppendLine("                }");
        }

        source.AppendLine();
        source.AppendLine("                return synapse;");
        source.Append("            }");
        return source.ToString();
    }

    private static string UnsupportedFactory(INamedTypeSymbol synapse)
    {
        var fullName = synapse.ToDisplayString(FullName);
        return
            $"static _ => throw new global::System.NotSupportedException(\"Cannot construct synapse '{StringLiteral(fullName)}' from Gherkin arguments because it has no supported public constructor or settable property shape.\")";
    }

    private static string? ArgumentExpression(
        ITypeSymbol type,
        string argumentName)
    {
        var argument =
            $"Argument(arguments, \"{StringLiteral(argumentName)}\")";

        return type.SpecialType switch
        {
            SpecialType.System_String => argument,
            SpecialType.System_Boolean =>
                $"global::System.Boolean.Parse({argument})",
            SpecialType.System_Byte =>
                $"global::System.Byte.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            SpecialType.System_SByte =>
                $"global::System.SByte.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            SpecialType.System_Int16 =>
                $"global::System.Int16.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            SpecialType.System_UInt16 =>
                $"global::System.UInt16.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            SpecialType.System_Int32 =>
                $"global::System.Int32.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            SpecialType.System_UInt32 =>
                $"global::System.UInt32.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            SpecialType.System_Int64 =>
                $"global::System.Int64.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            SpecialType.System_UInt64 =>
                $"global::System.UInt64.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            SpecialType.System_Single =>
                $"global::System.Single.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            SpecialType.System_Double =>
                $"global::System.Double.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            SpecialType.System_Decimal =>
                $"global::System.Decimal.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            _ when type.TypeKind == TypeKind.Enum =>
                $"global::System.Enum.Parse<global::{type.ToDisplayString(FullName)}>({argument}, ignoreCase: true)",
            _ when type.ToDisplayString(FullName) == "System.Guid" =>
                $"global::System.Guid.Parse({argument})",
            _ when type.ToDisplayString(FullName) == "System.DateTime" =>
                $"global::System.DateTime.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            _ when type.ToDisplayString(FullName) == "System.DateTimeOffset" =>
                $"global::System.DateTimeOffset.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            _ when type.ToDisplayString(FullName) == "System.TimeSpan" =>
                $"global::System.TimeSpan.Parse({argument}, global::System.Globalization.CultureInfo.InvariantCulture)",
            _ => null,
        };
    }

    private static string EscapeIdentifier(string value)
        => SyntaxFacts.GetKeywordKind(value) == SyntaxKind.None
            ? value
            : $"@{value}";

    private static string StringLiteral(string value)
        => value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");

    private static string TestVocabulary(VocabularyModel model)
    {
        var source = new StringBuilder();

        source.AppendLine("#nullable enable");
        source.AppendLine("using System.Diagnostics.CodeAnalysis;");
        source.AppendLine();
        source.AppendLine("namespace DigitalBrain.Generated;");
        source.AppendLine();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine("internal sealed class TestNeuronAccess");
        source.AppendLine("{");
        source.AppendLine("    internal TestNeuronAccess(");
        source.AppendLine("        global::DigitalBrain.Abstractions.NeuronId id,");
        source.AppendLine("        global::DigitalBrain.Testing.TestJournal incoming,");
        source.AppendLine("        global::DigitalBrain.Testing.TestJournal outgoing,");
        source.AppendLine("        global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task> restart)");
        source.AppendLine("    {");
        source.AppendLine("        Id = id;");
        source.AppendLine("        Incoming = incoming;");
        source.AppendLine("        Outgoing = outgoing;");
        source.AppendLine("        Restart = restart;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    internal global::DigitalBrain.Abstractions.NeuronId Id { get; }");
        source.AppendLine();
        source.AppendLine("    internal global::DigitalBrain.Testing.TestJournal Incoming { get; }");
        source.AppendLine();
        source.AppendLine("    internal global::DigitalBrain.Testing.TestJournal Outgoing { get; }");
        source.AppendLine();
        source.AppendLine("    internal global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task> Restart { get; }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine("internal sealed class TestNeuronContract");
        source.AppendLine("{");
        source.AppendLine("    internal TestNeuronContract(");
        source.AppendLine("        string identity,");
        source.AppendLine("        global::System.Func<global::DigitalBrain.Testing.TestOwner, string, TestNeuronAccess> open,");
        source.AppendLine("        global::System.Func<global::DigitalBrain.Client.IDigitalBrain, string, global::DigitalBrain.Abstractions.Synapse, global::System.Threading.Tasks.Task> send)");
        source.AppendLine("    {");
        source.AppendLine("        Identity = identity;");
        source.AppendLine("        Open = open;");
        source.AppendLine("        Send = send;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    internal string Identity { get; }");
        source.AppendLine();
        source.AppendLine("    internal global::System.Func<global::DigitalBrain.Testing.TestOwner, string, TestNeuronAccess> Open { get; }");
        source.AppendLine();
        source.AppendLine("    internal global::System.Func<global::DigitalBrain.Client.IDigitalBrain, string, global::DigitalBrain.Abstractions.Synapse, global::System.Threading.Tasks.Task> Send { get; }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine("internal sealed class TestSynapseObservation");
        source.AppendLine("{");
        source.AppendLine("    internal TestSynapseObservation(");
        source.AppendLine("        string correlationInstance,");
        source.AppendLine("        long sequence)");
        source.AppendLine("    {");
        source.AppendLine("        CorrelationInstance = correlationInstance;");
        source.AppendLine("        Sequence = sequence;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    internal string CorrelationInstance { get; }");
        source.AppendLine();
        source.AppendLine("    internal long Sequence { get; }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine("internal sealed class TestSynapseContract");
        source.AppendLine("{");
        source.AppendLine("    internal TestSynapseContract(");
        source.AppendLine("        string identity,");
        source.AppendLine("        global::System.Func<global::System.Collections.Generic.IReadOnlyDictionary<string, string>, global::DigitalBrain.Abstractions.Synapse> create,");
        source.AppendLine("        global::System.Func<global::DigitalBrain.Testing.TestJournal, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<TestSynapseObservation>> next,");
        source.AppendLine("        global::System.Func<global::DigitalBrain.Testing.TestJournal, long, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<int>> count)");
        source.AppendLine("    {");
        source.AppendLine("        Identity = identity;");
        source.AppendLine("        Create = create;");
        source.AppendLine("        Next = next;");
        source.AppendLine("        Count = count;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    internal string Identity { get; }");
        source.AppendLine();
        source.AppendLine("    internal global::System.Func<global::System.Collections.Generic.IReadOnlyDictionary<string, string>, global::DigitalBrain.Abstractions.Synapse> Create { get; }");
        source.AppendLine();
        source.AppendLine("    internal global::System.Func<global::DigitalBrain.Testing.TestJournal, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<TestSynapseObservation>> Next { get; }");
        source.AppendLine();
        source.AppendLine("    internal global::System.Func<global::DigitalBrain.Testing.TestJournal, long, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<int>> Count { get; }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine("internal static class GeneratedTestVocabulary");
        source.AppendLine("{");
        EmitNeuronDictionary(source, model.Neurons);
        source.AppendLine();
        EmitSynapseDictionary(source, model.Synapses);
        source.AppendLine();
        source.AppendLine("    internal static bool TryResolveNeuron(");
        source.AppendLine("        string name,");
        source.AppendLine("        [NotNullWhen(true)] out TestNeuronContract? contract)");
        source.AppendLine("        => Neurons.TryGetValue(name, out contract);");
        source.AppendLine();
        source.AppendLine("    internal static bool TryResolveSynapse(");
        source.AppendLine("        string name,");
        source.AppendLine("        [NotNullWhen(true)] out TestSynapseContract? contract)");
        source.AppendLine("        => Synapses.TryGetValue(name, out contract);");
        source.AppendLine();
        source.AppendLine("    internal static bool TryCreateSynapse(");
        source.AppendLine("        string name,");
        source.AppendLine("        global::System.Collections.Generic.IReadOnlyDictionary<string, string> arguments,");
        source.AppendLine("        [NotNullWhen(true)] out global::DigitalBrain.Abstractions.Synapse? synapse)");
        source.AppendLine("    {");
        source.AppendLine("        if (TryResolveSynapse(name, out var contract))");
        source.AppendLine("        {");
        source.AppendLine("            synapse = contract.Create(arguments);");
        source.AppendLine("            return true;");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        synapse = null;");
        source.AppendLine("        return false;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    private static string Argument(");
        source.AppendLine("        global::System.Collections.Generic.IReadOnlyDictionary<string, string> arguments,");
        source.AppendLine("        string name)");
        source.AppendLine("        => arguments.TryGetValue(name, out var value)");
        source.AppendLine("            ? value");
        source.AppendLine("            : throw new global::System.ArgumentException(");
        source.AppendLine("                $\"Required Gherkin argument '{name}' was not supplied.\",");
        source.AppendLine("                nameof(arguments));");
        source.AppendLine("}");

        return source.ToString();
    }

    private static void EmitNeuronDictionary(
        StringBuilder source,
        ImmutableArray<VocabularyNeuron> neurons)
    {
        var uniqueShortNames = new HashSet<string>(
            neurons
                .GroupBy(
                    static neuron => neuron.ShortName,
                    StringComparer.Ordinal)
                .Where(static group => group.Count() == 1)
                .Select(static group => group.Key),
            StringComparer.Ordinal);

        source.AppendLine("    private static readonly global::System.Collections.Generic.IReadOnlyDictionary<string, TestNeuronContract> Neurons =");
        source.AppendLine("        new global::System.Collections.Generic.Dictionary<string, TestNeuronContract>(global::System.StringComparer.Ordinal)");
        source.AppendLine("        {");

        foreach (var neuron in neurons)
        {
            EmitNeuronEntry(source, neuron.FullName, neuron);

            if (neuron.ShortName != neuron.FullName
                && uniqueShortNames.Contains(neuron.ShortName))
            {
                EmitNeuronEntry(source, neuron.ShortName, neuron);
            }
        }

        source.AppendLine("        };");
    }

    private static void EmitNeuronEntry(
        StringBuilder source,
        string key,
        VocabularyNeuron neuron)
    {
        source.AppendLine($"            [\"{StringLiteral(key)}\"] =");
        source.AppendLine("                new TestNeuronContract(");
        source.AppendLine($"                    \"{StringLiteral(neuron.FullName)}\",");
        source.AppendLine("                    static (owner, name) =>");
        source.AppendLine("                    {");
        source.AppendLine($"                        var neuron = owner.Neuron<global::{neuron.FullName}>(name);");
        source.AppendLine("                        return new TestNeuronAccess(");
        source.AppendLine("                            neuron.Id,");
        source.AppendLine("                            neuron.Incoming,");
        source.AppendLine("                            neuron.Outgoing,");
        source.AppendLine("                            neuron.RestartHostAsync);");
        source.AppendLine("                    },");
        source.AppendLine($"                    static (client, name, synapse) => client.SendAsync<global::{neuron.FullName}>(name, synapse)),");
    }

    private static void EmitSynapseDictionary(
        StringBuilder source,
        ImmutableArray<VocabularySynapse> synapses)
    {
        var uniqueShortNames = new HashSet<string>(
            synapses
                .GroupBy(
                    static synapse => synapse.ShortName,
                    StringComparer.Ordinal)
                .Where(static group => group.Count() == 1)
                .Select(static group => group.Key),
            StringComparer.Ordinal);

        source.AppendLine("    private static readonly global::System.Collections.Generic.IReadOnlyDictionary<string, TestSynapseContract> Synapses =");
        source.AppendLine("        new global::System.Collections.Generic.Dictionary<string, TestSynapseContract>(global::System.StringComparer.Ordinal)");
        source.AppendLine("        {");

        foreach (var synapse in synapses)
        {
            EmitSynapseEntry(source, synapse.FullName, synapse);

            if (synapse.ShortName != synapse.FullName
                && uniqueShortNames.Contains(synapse.ShortName))
            {
                EmitSynapseEntry(source, synapse.ShortName, synapse);
            }
        }

        source.AppendLine("        };");
    }

    private static void EmitSynapseEntry(
        StringBuilder source,
        string key,
        VocabularySynapse synapse)
    {
        source.AppendLine($"            [\"{StringLiteral(key)}\"] =");
        source.AppendLine("                new TestSynapseContract(");
        source.AppendLine($"                    \"{StringLiteral(synapse.FullName)}\",");
        source.AppendLine($"                    {synapse.Factory},");
        source.AppendLine("                    static async (journal, cancellationToken) =>");
        source.AppendLine("                    {");
        source.AppendLine($"                        var observed = await journal.NextAsync<global::{synapse.FullName}>(cancellationToken);");
        source.AppendLine("                        return new TestSynapseObservation(");
        source.AppendLine("                            observed.CorrelationId.Value.ToString(\"D\"),");
        source.AppendLine("                            observed.Sequence);");
        source.AppendLine("                    },");
        source.AppendLine("                    static async (journal, afterSequence, cancellationToken) =>");
        source.AppendLine($"                        (await journal.ReadAsync<global::{synapse.FullName}>(");
        source.AppendLine("                            afterSequence,");
        source.AppendLine("                            cancellationToken)).Count),");
    }

    private static CompositionModel CompositionOf(Compilation compilation)
    {
        var moduleContract = compilation.GetTypeByMetadataName(ModuleInterface);
        var compiledModuleContract = compilation.GetTypeByMetadataName(CompiledModuleInterface);

        if (moduleContract is null || compiledModuleContract is null)
        {
            return new CompositionModel([], emitCatalog: false, emitExtension: false);
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
        var emitExtension = compilation.AssemblyName != "DigitalBrain.Kernel"
            && !definesModule
            && siloBuilder is not null
            && compilation.GetTypeByMetadataName(DigitalBrainRuntime) is not null;

        return new CompositionModel(modules, emitCatalog: emitExtension, emitExtension);
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
        bool emitCatalog,
        bool emitExtension)
    {
        public ImmutableArray<ModuleModel> Modules { get; } = modules;

        public bool EmitCatalog { get; } = emitCatalog;

        public bool EmitExtension { get; } = emitExtension;
    }

    private readonly struct VocabularyNeuron(
        string fullName,
        string shortName)
    {
        public string FullName { get; } = fullName;

        public string ShortName { get; } = shortName;
    }

    private readonly struct VocabularySynapse(
        string fullName,
        string shortName,
        string factory)
    {
        public string FullName { get; } = fullName;

        public string ShortName { get; } = shortName;

        public string Factory { get; } = factory;
    }

    private readonly struct VocabularyAmbiguity(
        string kind,
        string shortName,
        ImmutableArray<string> candidates)
    {
        public string Kind { get; } = kind;

        public string ShortName { get; } = shortName;

        public ImmutableArray<string> Candidates { get; } = candidates;
    }

    private readonly struct VocabularyModel(
        ImmutableArray<VocabularyNeuron> neurons,
        ImmutableArray<VocabularySynapse> synapses,
        ImmutableArray<VocabularyAmbiguity> ambiguities,
        bool emit)
    {
        public ImmutableArray<VocabularyNeuron> Neurons { get; } = neurons;

        public ImmutableArray<VocabularySynapse> Synapses { get; } = synapses;

        public ImmutableArray<VocabularyAmbiguity> Ambiguities { get; } =
            ambiguities;

        public bool Emit { get; } = emit;
    }
}
