using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Contracts;

/// <summary>
/// DB0031: [Shareable] must be applied exclusively to contract interfaces,
/// and cross-brain invocations must target [Shareable] contracts.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ShareableContractAnalyzer : DiagnosticAnalyzer
{
    private const string ShareableAttribute = "DigitalBrain.Access.ShareableAttribute";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.ShareableContract);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!Rules.Has(type, ShareableAttribute))
        {
            return;
        }

        if (type.TypeKind != TypeKind.Interface)
        {
            var loc = type.Locations.Length > 0 ? type.Locations[0] : Location.None;
            context.ReportDiagnostic(Diagnostic.Create(Rules.ShareableContract, loc, type.Name));
            return;
        }

        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol method && (method.MethodKind == MethodKind.Ordinary || method.MethodKind == MethodKind.ExplicitInterfaceImplementation))
            {
                var loc = method.Locations.Length > 0 ? method.Locations[0] : Location.None;

                if (!IsValidReturnType(method.ReturnType, method.ReturnsVoid))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rules.ShareableContract, loc, method.Name));
                    continue;
                }

                foreach (var parameter in method.Parameters)
                {
                    if (parameter.RefKind is RefKind.Ref or RefKind.Out || !IsShareableType(parameter.Type))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rules.ShareableContract, loc, method.Name));
                        break;
                    }
                }
            }
        }
    }

    private static bool IsValidReturnType(ITypeSymbol returnType, bool returnsVoid)
    {
        if (returnsVoid || returnType.SpecialType == SpecialType.System_Void)
        {
            return true;
        }

        if (returnType is not INamedTypeSymbol namedReturn)
        {
            return false;
        }

        var originalDef = namedReturn.OriginalDefinition;
        var fullName = Rules.FullName(originalDef);

        if (fullName is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")
        {
            return true;
        }

        if (namedReturn.IsGenericType && namedReturn.TypeArguments.Length == 1 &&
            (fullName is "System.Threading.Tasks.Task`1" or "System.Threading.Tasks.ValueTask`1"))
        {
            return IsShareableType(namedReturn.TypeArguments[0]);
        }

        return false;
    }

    private static bool IsShareableType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        // Never allow System.Object or void as shareable data types
        if (type.SpecialType is SpecialType.System_Object or SpecialType.System_Void)
        {
            return false;
        }

        // Explicitly reject Stream or anything deriving from System.IO.Stream
        var namedSymbol = type as INamedTypeSymbol;
        var fullName = Rules.FullName(namedSymbol);
        if (fullName == "System.IO.Stream" || Rules.IsOrDerivesFrom(type, "System.IO.Stream"))
        {
            return false;
        }

        // Unwrap Nullable<T>
        if (namedSymbol is { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 })
        {
            return IsShareableType(namedSymbol.TypeArguments[0]);
        }

        // Arrays: byte[] or array of shareable types
        if (type is IArrayTypeSymbol arrayType)
        {
            return IsShareableType(arrayType.ElementType);
        }

        // Primitives and well-known special types
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Decimal:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_String:
            case SpecialType.System_DateTime:
                return true;
        }

        // Enum types
        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        // CancellationToken, Guid, DateTimeOffset, TimeSpan
        if (fullName is "System.Guid" or "System.DateTimeOffset" or "System.TimeSpan" or "System.Threading.CancellationToken")
        {
            return true;
        }

        // Types decorated with [Shareable]
        if (HasAttribute(type, ShareableAttribute, "Shareable"))
        {
            return true;
        }

        // Records
        if (namedSymbol is { IsRecord: true } || namedSymbol?.OriginalDefinition is { IsRecord: true })
        {
            return true;
        }

        // Types decorated with GenerateSerializerAttribute or AliasAttribute
        if (HasAttribute(type, "Orleans.GenerateSerializerAttribute", "GenerateSerializer") ||
            HasAttribute(type, "Orleans.AliasAttribute", "Alias"))
        {
            return true;
        }

        // Collections from System.Collections.Generic or System.Collections.Immutable where all type arguments are shareable
        if (namedSymbol is { IsGenericType: true })
        {
            var ns = namedSymbol.ContainingNamespace?.ToDisplayString();
            if (ns is "System.Collections.Generic" or "System.Collections.Immutable" or "System.Collections.ObjectModel")
            {
                return namedSymbol.TypeArguments.Length > 0 && namedSymbol.TypeArguments.All(IsShareableType);
            }
            if (ns == "System" && (namedSymbol.Name == "ValueTuple" || namedSymbol.Name == "Tuple"))
            {
                return namedSymbol.TypeArguments.Length > 0 && namedSymbol.TypeArguments.All(IsShareableType);
            }
        }

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, string fullAttributeName, string shortName)
    {
        if (CheckAttributes(symbol.GetAttributes(), fullAttributeName, shortName))
        {
            return true;
        }
        if (symbol is INamedTypeSymbol { IsGenericType: true } named && !SymbolEqualityComparer.Default.Equals(named, named.OriginalDefinition))
        {
            return CheckAttributes(named.OriginalDefinition.GetAttributes(), fullAttributeName, shortName);
        }
        return false;
    }

    private static bool CheckAttributes(ImmutableArray<AttributeData> attributes, string fullAttributeName, string shortName)
    {
        foreach (var attr in attributes)
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null) { continue; }
            if (Rules.FullName(attrClass) == fullAttributeName ||
                attrClass.Name == shortName ||
                attrClass.Name == shortName + "Attribute")
            {
                return true;
            }
        }
        return false;
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
        {
            return;
        }

        // Check if invoking Get<T>(key) with cross-brain key (e.g. "brain:..." or "cross:...")
        if (method.Name == "Get" && method.IsGenericMethod && method.TypeArguments.Length == 1)
        {
            var targetType = method.TypeArguments[0] as INamedTypeSymbol;
            if (targetType != null && targetType.TypeKind == TypeKind.Interface && !Rules.Has(targetType, ShareableAttribute))
            {
                if (invocation.ArgumentList.Arguments.Count > 0)
                {
                    var firstArg = invocation.ArgumentList.Arguments[0].Expression;
                    var constVal = context.SemanticModel.GetConstantValue(firstArg, context.CancellationToken);
                    if (constVal.HasValue && constVal.Value is string keyStr)
                    {
                        if (keyStr.StartsWith("brain:", StringComparison.OrdinalIgnoreCase) ||
                            keyStr.StartsWith("cross:", StringComparison.OrdinalIgnoreCase))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(Rules.ShareableContract, invocation.GetLocation(), targetType.Name));
                        }
                    }
                }
            }
        }
    }
}
