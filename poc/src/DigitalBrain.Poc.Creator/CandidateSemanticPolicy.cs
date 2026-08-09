using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Poc.Creator;

public static class CandidateSemanticPolicy
{
    public const string SocialPostObservedAlias = "db.poc.social.post-observed.v1";
    public const string AddChartPointAlias = "db.poc.chart.add-point.v1";

    private static readonly HashSet<string> ApprovedTypeDefinitions = new(StringComparer.Ordinal)
    {
        "System.DateTimeOffset",
        "System.Int32",
        "System.String",
        "System.StringComparison",
        "System.Threading.CancellationToken",
        "System.Threading.Tasks.Task",
        "System.Void",
        "DigitalBrain.Poc.Abstractions.IDigitalBrain",
        "DigitalBrain.Poc.Abstractions.IDurableState`1",
        "DigitalBrain.Poc.Abstractions.IHandle`1",
        "DigitalBrain.Poc.Abstractions.Neuron",
        "DigitalBrain.Poc.Abstractions.Neuron`1",
        "DigitalBrain.Poc.Abstractions.Synapse",
        "DigitalBrain.Poc.Charting.Contracts.AddChartPoint",
        "DigitalBrain.Poc.Charting.Contracts.ChartPointDraft",
        "DigitalBrain.Poc.Social.Contracts.SocialPostObserved",
        "Orleans.AliasAttribute",
        "Orleans.GenerateSerializerAttribute",
        "Orleans.IdAttribute",
    };

    internal static IReadOnlySet<string> TrustedAliases { get; } = new HashSet<string>(
        [
            "db.poc.probe.ingress.v1",
            "db.poc.other.ingress.v1",
            SocialPostObservedAlias,
            AddChartPointAlias,
            "db.poc.chart.point.v1",
            "db.poc.chart.point-draft.v1",
            "db.poc.chart.point-added.v1",
        ],
        StringComparer.Ordinal);

    internal static bool IsApprovedSymbol(ISymbol symbol, string candidateAssemblyName)
    {
        if (symbol is INamespaceSymbol or ILocalSymbol or IParameterSymbol or IRangeVariableSymbol)
        {
            return true;
        }

        if (string.Equals(
            symbol.ContainingAssembly?.Name,
            candidateAssemblyName,
            StringComparison.Ordinal))
        {
            return true;
        }

        return symbol switch
        {
            INamedTypeSymbol type => IsApprovedType(type),
            IMethodSymbol method => IsApprovedMethod(method),
            IPropertySymbol property => IsApprovedProperty(property),
            IFieldSymbol field => IsApprovedField(field),
            _ => false,
        };
    }

    private static bool IsApprovedType(INamedTypeSymbol type) =>
        ApprovedTypeDefinitions.Contains(TypeDefinitionName(type));

    private static bool IsApprovedMethod(IMethodSymbol method)
    {
        var typeName = TypeDefinitionName(method.ContainingType);
        if (method.MethodKind == MethodKind.Constructor)
        {
            return typeName is
                "DigitalBrain.Poc.Charting.Contracts.AddChartPoint" or
                "DigitalBrain.Poc.Charting.Contracts.ChartPointDraft" or
                "Orleans.AliasAttribute" or
                "Orleans.GenerateSerializerAttribute" or
                "Orleans.IdAttribute";
        }

        return (typeName, method.Name) switch
        {
            ("System.String", "Equals") => true,
            ("DigitalBrain.Poc.Abstractions.IDigitalBrain", "FireSynapse") => true,
            ("DigitalBrain.Poc.Abstractions.IDurableState`1", "Replace") => true,
            _ => false,
        };
    }

    private static bool IsApprovedProperty(IPropertySymbol property)
    {
        var typeName = TypeDefinitionName(property.ContainingType);
        return (typeName, property.Name) switch
        {
            ("System.Threading.Tasks.Task", "CompletedTask") => true,
            ("DigitalBrain.Poc.Abstractions.Neuron", "DigitalBrain") => true,
            ("DigitalBrain.Poc.Abstractions.Neuron`1", "DurableState") => true,
            ("DigitalBrain.Poc.Abstractions.IDurableState`1", "Value") => true,
            ("DigitalBrain.Poc.Social.Contracts.SocialPostObserved", "Author") => true,
            ("DigitalBrain.Poc.Social.Contracts.SocialPostObserved", "PostId") => true,
            ("DigitalBrain.Poc.Social.Contracts.SocialPostObserved", "OccurredAt") => true,
            _ => false,
        };
    }

    private static bool IsApprovedField(IFieldSymbol field)
    {
        var typeName = TypeDefinitionName(field.ContainingType);
        return typeName == "System.StringComparison" && field.Name == "OrdinalIgnoreCase";
    }

    private static string TypeDefinitionName(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        var prefix = definition.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : definition.ContainingNamespace.ToDisplayString() + ".";
        return prefix + definition.MetadataName;
    }
}
