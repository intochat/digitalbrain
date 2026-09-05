namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.behavior-admitted")]
public sealed record BehaviorAdmitted : Signal
{
    public BehaviorAdmitted(string name, string source, Guid revision = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Name = name.Trim();
        Source = source;
        Revision = revision;
    }

    [Id(0)]
    public string Name { get; }

    [Id(1)]
    public string Source { get; }

    [Id(2)]
    public Guid Revision { get; }
}
