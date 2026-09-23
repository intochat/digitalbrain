using Orleans;

namespace DigitalBrain.Contracts.Types;

[GenerateSerializer, Alias("digitalbrain.type-validation")]
public sealed class TypeValidationException(
    FieldKind kind,
    object? rejectedValue,
    IReadOnlyList<string> allowedValues)
    : ArgumentException(BuildMessage(kind, rejectedValue, allowedValues))
{
    public TypeValidationException(FieldKind kind, object? rejectedValue)
        : this(kind, rejectedValue, TypeCatalog.AllowedFor(kind))
    {
    }

    [Id(0)] public FieldKind Kind { get; } = kind;

    [Id(1)] public string[] AllowedValues { get; } = [.. allowedValues];

    [Id(2)] public object? RejectedValue { get; } = kind == FieldKind.Secret ? null : Display(rejectedValue);

    private static object? Display(object? rejectedValue) => rejectedValue?.ToString();

    private static string BuildMessage(FieldKind kind, object? rejectedValue, IReadOnlyList<string> allowedValues)
    {
        var shown = kind == FieldKind.Secret ? "(redacted)" : $"'{rejectedValue}'";
        return $"{shown} is not a valid {kind} value. Allowed {kind} values: {string.Join(", ", allowedValues)}.";
    }
}
