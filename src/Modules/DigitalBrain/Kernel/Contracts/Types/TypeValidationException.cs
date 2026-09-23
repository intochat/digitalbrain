namespace DigitalBrain.Contracts.Types;

public sealed class TypeValidationException : ArgumentException
{
    public TypeValidationException(FieldKind kind, object? rejectedValue)
        : this(kind, rejectedValue, TypeCatalog.AllowedFor(kind))
    {
    }

    public TypeValidationException(FieldKind kind, object? rejectedValue, IReadOnlyList<string> allowedValues)
        : base(BuildMessage(kind, rejectedValue, allowedValues))
    {
        Kind = kind;
        AllowedValues = allowedValues;
        RejectedValue = kind == FieldKind.Secret ? null : rejectedValue;
    }

    public FieldKind Kind { get; }

    public object? RejectedValue { get; }

    public IReadOnlyList<string> AllowedValues { get; }

    private static string BuildMessage(FieldKind kind, object? rejectedValue, IReadOnlyList<string> allowedValues)
    {
        var shown = kind == FieldKind.Secret ? "(redacted)" : $"'{rejectedValue}'";
        return $"{shown} is not a valid {kind} value. Allowed {kind} values: {string.Join(", ", allowedValues)}.";
    }
}