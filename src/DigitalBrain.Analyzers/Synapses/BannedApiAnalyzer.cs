using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DigitalBrain.Analyzers.Synapses;

/// <summary>DB0045: a synapse assembly (one that declares a <c>[State]</c> record) uses an API from <see cref="BannedApis"/>.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BannedApiAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.BannedApi);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!NoOnInSynapseAssemblyAnalyzer.DeclaresState(start.Compilation.Assembly.GlobalNamespace)) { return; }
            start.RegisterOperationAction(AnalyzeOperation,
                OperationKind.Invocation, OperationKind.ObjectCreation, OperationKind.PropertyReference,
                OperationKind.FieldReference, OperationKind.EventReference, OperationKind.MethodReference);
            start.RegisterSyntaxNodeAction(AnalyzeUsing, SyntaxKind.UsingDirective);
        });
    }

    private static void AnalyzeOperation(OperationAnalysisContext context)
    {
        ISymbol? member = context.Operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IObjectCreationOperation creation => creation.Constructor,
            IMemberReferenceOperation reference => reference.Member,
            _ => null,
        };
        if (member?.ContainingType is not { } type) { return; }

        var typeName = TypeName(type);
        if (BannedApis.IsBannedMember(typeName, member.Name))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.BannedApi, context.Operation.Syntax.GetLocation(), typeName + "." + member.Name));
        }
    }

    private static void AnalyzeUsing(SyntaxNodeAnalysisContext context)
    {
        var directive = (UsingDirectiveSyntax)context.Node;
        if (directive.StaticKeyword.IsKind(SyntaxKind.None)) { return; }
        if (context.SemanticModel.GetSymbolInfo(directive.Name!, context.CancellationToken).Symbol is not INamedTypeSymbol type) { return; }

        var typeName = TypeName(type);
        if (BannedApis.IsBannedType(typeName))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.BannedApi, directive.GetLocation(), typeName));
        }
    }

    // Namespace plus containing types, generic arity dropped: the same shape the host reads from metadata.
    private static string TypeName(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        var name = type.Name;
        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            name = outer.Name + "." + name;
        }
        return type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + name : name;
    }
}
