namespace DigitalBrain;

internal readonly record struct ScopeKey
{
    internal ScopeKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    internal string Value { get; }
}
