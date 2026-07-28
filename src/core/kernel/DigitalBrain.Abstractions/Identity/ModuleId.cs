namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.module-id")]
public readonly record struct ModuleId
{
    public ModuleId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;
}
