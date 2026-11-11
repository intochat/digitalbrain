using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Synapses;

/// <summary>DB0041: a <c>[Synapse]</c> method is a reaction or a view; the rules mirror DigitalBrain.SynapseShape over symbols.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SynapseShapeAnalyzer : DiagnosticAnalyzer
{
    private const string CancellationTokenType = "System.Threading.CancellationToken";
    private const string TaskType = "System.Threading.Tasks.Task`1";
    private const string ReactionType = "DigitalBrain.Reaction`1";
    private const string EnvelopeType = "DigitalBrain.SignalEnvelope";
    private const string AsksType = "DigitalBrain.Units.Asks`1";
    private const string AnswerType = "DigitalBrain.Units.Answer";
    private const string AnsweredType = "DigitalBrain.Units.Answered";

    // Mirrors DigitalBrain.Time.Schedule: HH:mm, Day HH:mm, every Nm / every Nh (N from 1 to 999999).
    private static readonly Regex ScheduleRule = new Regex(
        @"^(?:(?:[01][0-9]|2[0-3]):[0-5][0-9]|(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun) (?:[01][0-9]|2[0-3]):[0-5][0-9]|every (?<n>[0-9]{1,6})[mh])\z",
        RegexOptions.CultureInvariant);

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

        // [Synapse(Type from, string into)] declares a signal; [Synapse(string signalAlias, string into)] an alias reaction; [Synapse(string from)] is a view.
        var args = attribute.ConstructorArguments;
        var hasSignal = args.Length > 1;
        var byAlias = hasSignal && args[0].Value is string;
        var into = (hasSignal ? args[1] : args[0]).Value as string ?? string.Empty;
        var slash = into.IndexOf('/');
        var alias = slash < 0 ? into : into.Substring(0, slash);
        var state = FindState(compilation, alias);

        var signals = 0;
        var envelopes = 0;
        var stateCount = 0;
        var tokens = 0;
        var deferredSlots = 0;
        var callingSlots = 0;
        var answers = 0;
        var unknown = false;
        var misplacedToken = false;
        var hasSignalParameter = false;
        ITypeSymbol? signalParameter = null;
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var type = method.Parameters[i].Type;
            if (Rules.Has(method.Parameters[i], Rules.SlotAttribute))
            {
                var deferred = type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } asks
                    && Rules.FullName(asks.OriginalDefinition) == AsksType;
                var contract = deferred ? ((INamedTypeSymbol)type).TypeArguments[0] : type;
                if (contract.TypeKind != TypeKind.Interface || !Rules.Has(contract, Rules.CapabilityAttribute))
                {
                    return $"a {Rules.SynapseReasons.SlotContractNeedsCapability}.";
                }

                if (deferred) { deferredSlots++; } else { callingSlots++; }
            }
            else if (Rules.IsOrDerivesFrom(type, Rules.SignalType))
            {
                signals++;
                hasSignalParameter = true;
                signalParameter = type;
            }
            else if (byAlias && Rules.FullName(type as INamedTypeSymbol) == EnvelopeType)
            {
                envelopes++;
            }
            else if (Rules.FullName(type as INamedTypeSymbol) == CancellationTokenType)
            {
                tokens++;
                misplacedToken |= i != method.Parameters.Length - 1;
            }
            else if (Rules.FullName(type as INamedTypeSymbol) == AnswerType)
            {
                answers++;
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

        if (byAlias && (signals > 0 || envelopes != 1))
        {
            return $"an {Rules.SynapseReasons.AliasNeedsEnvelope}.";
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
        var isReaction = SymbolEqualityComparer.Default.Equals(returned, state)
            || (returned is INamedTypeSymbol { IsGenericType: true } generic
                && Rules.FullName(generic.OriginalDefinition) == ReactionType
                && SymbolEqualityComparer.Default.Equals(generic.TypeArguments[0], state));
        var isAsync = !SymbolEqualityComparer.Default.Equals(returned, method.ReturnType);
        if (deferredSlots > 0 && (!isReaction || isAsync || tokens > 0))
        {
            return $"an {Rules.SynapseReasons.AsksNeedSyncReaction}.";
        }

        if (deferredSlots > 0 && callingSlots > 0)
        {
            return $"an {Rules.SynapseReasons.AsksExcludeCallingSlot}.";
        }

        var declared = hasSignal && !byAlias ? args[0].Value as INamedTypeSymbol : null;
        if (answers > 0 && (answers > 1 || Rules.FullName(declared) != AnsweredType || !isReaction || isAsync || tokens > 0 || callingSlots > 0))
        {
            return $"an {Rules.SynapseReasons.AnswerNeedsAnswered}.";
        }

        if (byAlias)
        {
            return isReaction ? EveryRefusal(attribute, isReaction: false, declared: null) : $"an {Rules.SynapseReasons.AliasNeedsEnvelope}.";
        }

        if (!isReaction)
        {
            if (hasSignalParameter || tokens > 0 || hasSignal)
            {
                return $"a {Rules.SynapseReasons.ViewTakesOnlyState}; a function with a signal must return the state.";
            }

            return EveryRefusal(attribute, isReaction: false, declared: null);
        }

        if (!hasSignalParameter || !hasSignal)
        {
            return $"a {Rules.SynapseReasons.GateNeedsSignal} (a reaction as well), declared by [Synapse] and taken as a parameter.";
        }

        if (declared is not null && signalParameter is INamedTypeSymbol parameter
            && !Rules.IsOrDerivesFrom(declared, Rules.FullName(parameter)))
        {
            return $"the {Rules.SynapseReasons.SignalFitsParameter}.";
        }

        return EveryRefusal(attribute, isReaction, declared);
    }

    private static string? EveryRefusal(AttributeData attribute, bool isReaction, INamedTypeSymbol? declared)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key != "Every") { continue; }
            if (named.Value.Value is not string text) { return null; }

            if (!isReaction || Rules.FullName(declared) != Rules.TickType)
            {
                return $"an {Rules.SynapseReasons.EveryNeedsTick}.";
            }

            var match = ScheduleRule.Match(text);
            var valid = match.Success
                && (!match.Groups["n"].Success || int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) >= 1);
            return valid ? null : $"a {Rules.SynapseReasons.Schedule}: {text} is not HH:mm, Mon HH:mm, every 5m or every 2h.";
        }

        return null;
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
