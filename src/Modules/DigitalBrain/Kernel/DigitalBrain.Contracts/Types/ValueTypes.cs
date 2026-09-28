namespace DigitalBrain.Contracts.Types;

public readonly record struct PlainText(string Value);

public readonly record struct LongText(string Value);

public readonly record struct Number(decimal Value);

public readonly record struct Date(DateOnly Value);

public readonly record struct DateTimeValue(DateTime Value);

public readonly record struct BooleanValue(bool Value);

public readonly record struct Choice(string Value);

public readonly record struct MultiChoice(IReadOnlyList<string> Values);

public readonly record struct Email(string Value);

public readonly record struct Url(string Value);

public readonly record struct Reference(string EntityId, string? Label = null);