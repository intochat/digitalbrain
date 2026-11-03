namespace DigitalBrain.Modules.Options;

// A module's name on every channel: configuration sections, registration, hosting, deployment.
// Valid by construction; configuration keys are case-insensitive, so equality is too.
public readonly record struct ModuleId
{
    public ModuleId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
        { throw new ArgumentException($"'{value}' is not a module id: ASCII letters, digits, hyphens.", nameof(value)); }
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
    public bool Equals(ModuleId other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);
}
