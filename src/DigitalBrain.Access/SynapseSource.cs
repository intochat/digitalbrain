namespace DigitalBrain;

public readonly record struct SynapseSource
{
    public SynapseSource(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
}
