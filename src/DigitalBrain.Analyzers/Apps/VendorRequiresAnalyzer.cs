using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DigitalBrain.Analyzers.Apps;

/// <summary>Module (vendor) ids that are never capability names.</summary>
internal static class VendorIds
{
    public static readonly ImmutableHashSet<string> Known = ImmutableHashSet.Create(StringComparer.Ordinal,
        "google-drive", "google.drive", "gdrive", "gmail", "google-calendar", "google.calendar",
        "microsoft.onedrive", "onedrive", "outlook", "microsoft.outlook", "sharepoint", "teams",
        "dropbox", "box", "s3", "aws.s3", "azure-blob", "azure.blob", "azure-storage",
        "postgres", "postgresql", "mysql", "sqlserver", "sqlite", "cosmos", "redis", "mongodb",
        "windows", "macos", "linux", "salesforce", "slack", "github", "notion", "openai", "anthropic");
}

/// <summary>
/// DB0021: a capability id built in an app (<c>CapabilityId.Parse</c>, <c>TryParse</c> or the constructor) names a
/// module id: a known vendor id or a <c>[Module("id")]</c> declared in the same compilation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VendorRequiresAnalyzer : DiagnosticAnalyzer
{
    private const string CapabilityIdType = "DigitalBrain.Capabilities.CapabilityId";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(AppRules.ModuleRequirement);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var modules = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            CollectModules(start.Compilation.Assembly.GlobalNamespace, modules);
            var declared = modules.ToImmutable();
            start.RegisterOperationAction(c => AnalyzeInvocation(c, declared), OperationKind.Invocation);
            start.RegisterOperationAction(c => AnalyzeCreation(c, declared), OperationKind.ObjectCreation);
        });
    }

    private static void CollectModules(INamespaceSymbol ns, ImmutableHashSet<string>.Builder modules)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol child) { CollectModules(child, modules); continue; }
            foreach (var data in member.GetAttributes())
            {
                if (Rules.FullName(data.AttributeClass) == Rules.ModuleAttribute
                    && data.ConstructorArguments.Length == 1 && data.ConstructorArguments[0].Value is string id)
                {
                    modules.Add(id);
                }
            }
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ImmutableHashSet<string> declared)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name is not ("Parse" or "TryParse") || Rules.FullName(method.ContainingType) != CapabilityIdType) { return; }
        if (invocation.Arguments.Length == 0) { return; }
        Check(context, invocation.Arguments[0].Value, nameOnly: false, declared);
    }

    private static void AnalyzeCreation(OperationAnalysisContext context, ImmutableHashSet<string> declared)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (Rules.FullName(creation.Type as INamedTypeSymbol) != CapabilityIdType || creation.Arguments.Length == 0) { return; }
        Check(context, creation.Arguments[0].Value, nameOnly: true, declared);
    }

    private static void Check(OperationAnalysisContext context, IOperation value, bool nameOnly, ImmutableHashSet<string> declared)
    {
        var app = AppRules.ContainingApp(context.ContainingSymbol);
        if (app is null || !value.ConstantValue.HasValue || value.ConstantValue.Value is not string text) { return; }
        var name = text;
        if (!nameOnly)
        {
            var at = text.IndexOf('@');
            if (at >= 0) { name = text.Substring(0, at); }
        }
        if (VendorIds.Known.Contains(name) || declared.Contains(name))
        {
            context.ReportDiagnostic(Diagnostic.Create(AppRules.ModuleRequirement, value.Syntax.GetLocation(), app.Name, name));
        }
    }
}
