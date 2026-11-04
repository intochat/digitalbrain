using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Synapses;

/// <summary>DB0043: a reaction returns a state record that carries a status-like <c>string</c>, <c>bool</c> or enum member.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StatusStringAnalyzer : DiagnosticAnalyzer
{
    private const string TaskType = "System.Threading.Tasks.Task`1";
    private const string ReactionType = "DigitalBrain.Reaction`1";
    private const string ViewType = "DigitalBrain.UI.View";

    private static readonly Regex StatusLike = new Regex("status|error|failed|rejected|valid", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.StatusString);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var reported = new ConcurrentDictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);
            start.RegisterSymbolAction(symbol => Analyze(symbol, reported), SymbolKind.Method);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ConcurrentDictionary<INamedTypeSymbol, bool> reported)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!Rules.Has(method, Rules.SynapseAttribute)) { return; }

        var returned = method.ReturnType;
        if (returned is INamedTypeSymbol { IsGenericType: true } task && Rules.FullName(task.OriginalDefinition) == TaskType)
        {
            returned = task.TypeArguments[0];
        }

        if (Rules.FullName(returned as INamedTypeSymbol) == ViewType) { return; }

        if (returned is INamedTypeSymbol { IsGenericType: true } reaction && Rules.FullName(reaction.OriginalDefinition) == ReactionType)
        {
            returned = reaction.TypeArguments[0];
        }

        if (returned is not INamedTypeSymbol state || !Rules.Has(state, Rules.StateAttribute)) { return; }

        foreach (var member in state.GetMembers())
        {
            if (member is not IPropertySymbol { DeclaredAccessibility: Accessibility.Public, IsStatic: false } property) { continue; }
            if (!StatusLike.IsMatch(property.Name) || !IsStatusType(property.Type) || !property.Locations[0].IsInSource) { continue; }

            if (reported.TryAdd(state, true))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rules.StatusString, property.Locations[0], state.ToDisplayString(), property.Name));
            }
            return;
        }
    }

    private static bool IsStatusType(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean || type.TypeKind == TypeKind.Enum;
}
