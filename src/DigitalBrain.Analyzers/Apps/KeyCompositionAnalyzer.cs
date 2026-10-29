using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DigitalBrain.Analyzers.Apps;

/// <summary>
/// DB0023: inside an app, a key-named value (a local, parameter, field or property whose name contains "key") is
/// joined with a '/'-bearing string by <c>+</c>, interpolation or <c>string.Concat</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KeyCompositionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(AppRules.StringKey);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeBinary, OperationKind.Binary);
        context.RegisterOperationAction(AnalyzeInterpolation, OperationKind.InterpolatedString);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeBinary(OperationAnalysisContext context)
    {
        var binary = (IBinaryOperation)context.Operation;
        if (!IsStringAdd(binary)) { return; }
        // Report the outermost concatenation only.
        if (binary.Parent is IBinaryOperation parent && IsStringAdd(parent)) { return; }
        var parts = Flatten(binary).ToList();
        Report(context, parts.Select(KeyName).FirstOrDefault(n => n is not null), parts.Any(HasSlash));
    }

    private static void AnalyzeInterpolation(OperationAnalysisContext context)
    {
        var interpolated = (IInterpolatedStringOperation)context.Operation;
        string? key = null;
        var slash = false;
        foreach (var part in interpolated.Parts)
        {
            if (part is IInterpolatedStringTextOperation text) { slash |= HasSlash(text.Text); }
            else if (part is IInterpolationOperation hole) { key ??= KeyName(hole.Expression); slash |= HasSlash(hole.Expression); }
        }
        Report(context, key, slash);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod.Name != "Concat" || invocation.TargetMethod.ContainingType.SpecialType != SpecialType.System_String) { return; }
        var values = new List<IOperation>();
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Value is IArrayCreationOperation { Initializer: { } init }) { values.AddRange(init.ElementValues); }
            else { values.Add(argument.Value); }
        }
        Report(context, values.Select(KeyName).FirstOrDefault(n => n is not null), values.Any(HasSlash));
    }

    private static void Report(OperationAnalysisContext context, string? key, bool slash)
    {
        if (key is null || !slash) { return; }
        var app = AppRules.ContainingApp(context.ContainingSymbol);
        if (app is null) { return; }
        context.ReportDiagnostic(Diagnostic.Create(AppRules.StringKey, context.Operation.Syntax.GetLocation(), app.Name, key));
    }

    private static bool IsStringAdd(IBinaryOperation binary)
        => binary.OperatorKind == BinaryOperatorKind.Add && binary.Type?.SpecialType == SpecialType.System_String;

    private static IEnumerable<IOperation> Flatten(IOperation operation)
    {
        var value = Unwrap(operation);
        if (value is IBinaryOperation binary && IsStringAdd(binary))
        {
            foreach (var left in Flatten(binary.LeftOperand)) { yield return left; }
            foreach (var right in Flatten(binary.RightOperand)) { yield return right; }
        }
        else
        {
            yield return value;
        }
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conversion) { operation = conversion.Operand; }
        return operation;
    }

    private static bool HasSlash(IOperation operation)
        => Unwrap(operation).ConstantValue is { HasValue: true, Value: string text } && text.IndexOf('/') >= 0;

    private static string? KeyName(IOperation operation)
    {
        var name = Unwrap(operation) switch
        {
            ILocalReferenceOperation local => local.Local.Name,
            IParameterReferenceOperation parameter => parameter.Parameter.Name,
            IFieldReferenceOperation field => field.Field.Name,
            IPropertyReferenceOperation property => property.Property.Name,
            _ => null,
        };
        return name is not null && name.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0 ? name : null;
    }
}
