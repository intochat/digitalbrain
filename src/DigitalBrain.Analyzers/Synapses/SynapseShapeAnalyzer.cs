using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Synapses;

/// <summary>DB0041: a <c>[Synapse]</c> method is a gate, a reaction or a view; the rules mirror DigitalBrain.SynapseShape over symbols.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SynapseShapeAnalyzer : DiagnosticAnalyzer
{
    private const string CancellationTokenType = "System.Threading.CancellationToken";
    private const string TaskType = "System.Threading.Tasks.Task`1";
    private const string ReactionType = "DigitalBrain.Reaction`1";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.SynapseShape);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.Method);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        var attribute = FindAttribute(method);
        if (attribute is null || attribute.ConstructorArguments.Length == 0) { return; }

        var reason = Refusal(method, attribute, context.Compilation);
        if (reason is null) { return; }

        var owner = method.ContainingType?.ToDisplayString();
        var name = owner is null ? method.Name : owner + "." + method.Name;
        context.ReportDiagnostic(Diagnostic.Create(Rules.SynapseShape, method.Locations[0], name, reason));
    }

    private static AttributeData? FindAttribute(IMethodSymbol method)
    {
        foreach (var data in method.GetAttributes())
        {
            if (Rules.FullName(data.AttributeClass) == Rules.SynapseAttribute) { return data; }
        }
        return null;
    }

    private static string? Refusal(IMethodSymbol method, AttributeData attribute, Compilation compilation)
    {
        if (method.DeclaredAccessibility != Accessibility.Public || !method.IsStatic)
        {
            return $"a synapse function must be {Rules.SynapseReasons.PublicStatic}.";
        }

        if (method.ContainingType is { ContainingType: { } outer } && !outer.IsStatic)
        {
            return $"a synapse function must be {Rules.SynapseReasons.PublicStatic} on a static class or a top-level type, not nested in a non-static class.";
        }

        // [Synapse(Type from, string into)] declares a signal; [Synapse(string from)] is a view.
        var args = attribute.ConstructorArguments;
        var hasSignal = args.Length > 1;
        var into = (hasSignal ? args[1] : args[0]).Value as string ?? string.Empty;
        var slash = into.IndexOf('/');
        var alias = slash < 0 ? into : into.Substring(0, slash);
        var state = FindState(compilation, alias);

        var signals = 0;
        var stateCount = 0;
        var tokens = 0;
        var unknown = false;
        var misplacedToken = false;
        var hasSignalParameter = false;
        ITypeSymbol? signalParameter = null;
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var type = method.Parameters[i].Type;
            if (Rules.IsOrDerivesFrom(type, Rules.SignalType))
            {
                signals++;
                hasSignalParameter = true;
                signalParameter = type;
            }
            else if (Rules.FullName(type as INamedTypeSymbol) == CancellationTokenType)
            {
                tokens++;
                misplacedToken |= i != method.Parameters.Length - 1;
            }
            else if (state is not null && SymbolEqualityComparer.Default.Equals(type, state))
            {
                stateCount++;
            }
            else
            {
                unknown = true;
            }
        }

        if (signals > 1)
        {
            return $"a synapse takes {Rules.SynapseReasons.AtMostOneSignal}.";
        }

        if (state is null || stateCount != 1)
        {
            return $"a synapse takes exactly one {Rules.SynapseReasons.StateRecord} '{alias}' parameter.";
        }

        if (unknown || tokens > 1 || misplacedToken)
        {
            return $"a parameter is {Rules.SynapseReasons.UnknownParameter}.";
        }

        var returned = Unwrap(method.ReturnType);
        var isGate = Rules.FullName(returned as INamedTypeSymbol) == Rules.CheckType;
        var isReaction = SymbolEqualityComparer.Default.Equals(returned, state)
            || (returned is INamedTypeSymbol { IsGenericType: true } generic
                && Rules.FullName(generic.OriginalDefinition) == ReactionType
                && SymbolEqualityComparer.Default.Equals(generic.TypeArguments[0], state));

        if (!isGate && !isReaction)
        {
            return hasSignalParameter || tokens > 0 || hasSignal
                ? $"a {Rules.SynapseReasons.ViewTakesOnlyState}; a function with a signal must return Check or the state."
                : null;
        }

        if (!hasSignalParameter || !hasSignal)
        {
            return $"a {Rules.SynapseReasons.GateNeedsSignal} (a reaction as well), declared by [Synapse] and taken as a parameter.";
        }

        return args[0].Value is INamedTypeSymbol declared && signalParameter is INamedTypeSymbol parameter
            && !Rules.IsOrDerivesFrom(declared, Rules.FullName(parameter))
            ? $"the {Rules.SynapseReasons.SignalFitsParameter}."
            : null;
    }

    private static ITypeSymbol Unwrap(ITypeSymbol returned) =>
        returned is INamedTypeSymbol { IsGenericType: true } generic && Rules.FullName(generic.OriginalDefinition) == TaskType
            ? generic.TypeArguments[0]
            : returned;

    private static INamedTypeSymbol? FindState(Compilation compilation, string alias)
    {
        // Source first; referenced assemblies only when the alias is not declared here.
        return Find(compilation.Assembly.GlobalNamespace, alias)
            ?? compilation.SourceModule.ReferencedAssemblySymbols.Select(assembly => Find(assembly.GlobalNamespace, alias)).FirstOrDefault(found => found is not null);
    }

    private static INamedTypeSymbol? Find(INamespaceOrTypeSymbol container, string alias)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol or INamedTypeSymbol)
            {
                if (member is INamedTypeSymbol type && IsState(type, alias)) { return type; }
                var nested = Find((INamespaceOrTypeSymbol)member, alias);
                if (nested is not null) { return nested; }
            }
        }
        return null;
    }

    private static bool IsState(INamedTypeSymbol type, string alias)
    {
        foreach (var data in type.GetAttributes())
        {
            if (Rules.FullName(data.AttributeClass) == Rules.StateAttribute
                && data.ConstructorArguments.Length == 1
                && data.ConstructorArguments[0].Value as string == alias)
            {
                return true;
            }
        }
        return false;
    }
}
