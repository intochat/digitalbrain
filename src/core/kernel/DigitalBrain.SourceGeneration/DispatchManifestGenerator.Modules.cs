using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.SourceGeneration;

public sealed partial class DispatchManifestGenerator
{
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

    private static ModuleCapsuleModel? ModuleOf(GeneratorSyntaxContext syntax, CancellationToken cancellationToken)
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
            production.ReportDiagnostic(Diagnostic.Create(ModuleMustBePartial, module.Location, module.FullName));
            valid = false;
        }

        if (module.IsNested)
        {
            production.ReportDiagnostic(Diagnostic.Create(ModuleMustBeTopLevel, module.Location, module.FullName));
            valid = false;
        }

        if (module.IsGeneric)
        {
            production.ReportDiagnostic(Diagnostic.Create(ModuleMustBeNonGeneric, module.Location, module.FullName));
            valid = false;
        }

        if (!module.HasPublicParameterlessConstructor)
        {
            production.ReportDiagnostic(Diagnostic.Create(ModuleNeedsPublicConstructor, module.Location, module.FullName));
            valid = false;
        }

        if (!valid)
        {
            return;
        }

        production.AddSource($"{module.FullName}.CompiledModule.g.cs", CompiledModule(module));
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
}
