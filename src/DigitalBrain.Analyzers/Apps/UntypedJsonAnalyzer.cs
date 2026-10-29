using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DigitalBrain.Analyzers.Apps;

/// <summary>
/// DB0025: inside an app, JSON is written by hand instead of through typed records:
/// <c>JsonSerializer.Serialize*</c> on <c>object</c> or an anonymous type, any use of <c>System.Text.Json.Nodes</c>
/// (<c>JsonNode</c>, <c>JsonObject</c>, <c>JsonArray</c>, <c>JsonValue</c>) or <c>JsonDocument</c>, and JSON text
/// built from strings (a concatenation or interpolation whose literal parts contain <c>{"</c> or <c>":</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UntypedJsonAnalyzer : DiagnosticAnalyzer
{
    private const string Json = "System.Text.Json";
    private const string Nodes = "System.Text.Json.Nodes";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(AppRules.HandWrittenJson);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeCreation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(AnalyzeBinary, OperationKind.Binary);
        context.RegisterOperationAction(AnalyzeInterpolation, OperationKind.InterpolatedString);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var owner = method.ContainingType;
        if (IsUntypedJsonType(owner))
        {
            // Report the entry point (a static Parse/Create) once; calls on the node it returns are the same use.
            if (!method.IsStatic) { return; }
            Report(context, owner.Name + "." + method.Name);
            return;
        }
        if (owner.Name != "JsonSerializer" || Namespace(owner) != Json || !method.Name.StartsWith("Serialize", StringComparison.Ordinal)) { return; }
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is null || argument.Parameter.Ordinal != 0) { continue; }
            var value = argument.Value;
            while (value is IConversionOperation conversion) { value = conversion.Operand; }
            if (value.Type is { } valueType && (valueType.SpecialType == SpecialType.System_Object || valueType.IsAnonymousType))
            {
                Report(context, "JsonSerializer." + method.Name);
            }
        }
    }

    private static void AnalyzeCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is INamedTypeSymbol type && IsUntypedJsonType(type)) { Report(context, "new " + type.Name); }
    }

    private static void AnalyzeBinary(OperationAnalysisContext context)
    {
        var binary = (IBinaryOperation)context.Operation;
        if (!IsStringAdd(binary) || (binary.Parent is IBinaryOperation parent && IsStringAdd(parent))) { return; }
        if (HasJsonLiteral(binary)) { Report(context, "string concatenation"); }
    }

    private static void AnalyzeInterpolation(OperationAnalysisContext context)
    {
        var interpolated = (IInterpolatedStringOperation)context.Operation;
        foreach (var part in interpolated.Parts)
        {
            if (part is IInterpolatedStringTextOperation { Text: { } text } && IsJsonText(text.ConstantValue))
            {
                Report(context, "string interpolation");
                return;
            }
        }
    }

    private static bool HasJsonLiteral(IOperation operation)
    {
        while (operation is IConversionOperation conversion) { operation = conversion.Operand; }
        if (operation is IBinaryOperation binary && IsStringAdd(binary)) { return HasJsonLiteral(binary.LeftOperand) || HasJsonLiteral(binary.RightOperand); }
        return IsJsonText(operation.ConstantValue);
    }

    private static bool IsJsonText(Optional<object?> constant)
        => constant.HasValue && constant.Value is string text
            && (text.IndexOf("{\"", StringComparison.Ordinal) >= 0 || text.IndexOf("\":", StringComparison.Ordinal) >= 0);

    private static bool IsStringAdd(IBinaryOperation binary)
        => binary.OperatorKind == BinaryOperatorKind.Add && binary.Type?.SpecialType == SpecialType.System_String;

    private static bool IsUntypedJsonType(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var ns = Namespace(current);
            if (ns == Nodes || (ns == Json && current.Name == "JsonDocument")) { return true; }
        }
        return false;
    }

    private static string Namespace(INamedTypeSymbol type) => type.ContainingNamespace?.ToDisplayString() ?? string.Empty;

    private static void Report(OperationAnalysisContext context, string what)
    {
        var app = AppRules.ContainingApp(context.ContainingSymbol);
        if (app is null) { return; }
        context.ReportDiagnostic(Diagnostic.Create(AppRules.HandWrittenJson, context.Operation.Syntax.GetLocation(), app.Name, what));
    }
}
