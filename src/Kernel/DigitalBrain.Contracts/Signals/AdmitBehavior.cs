namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.admit-behavior")]
public sealed record AdmitBehavior : Signal
{
    public AdmitBehavior(string name, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Name = name.Trim();
        Source = source;
    }

    [Id(0)]
    public string Name { get; }

    [Id(1)]
    public string Source { get; }
}
