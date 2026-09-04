namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.behavior-admitted")]
public sealed record BehaviorAdmitted : Signal
{
    public BehaviorAdmitted(string name, string source)
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
