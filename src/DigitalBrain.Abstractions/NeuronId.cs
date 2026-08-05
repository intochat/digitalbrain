namespace DigitalBrain;

public readonly record struct NeuronId(string Kind, string Name)
{
    public static string KindOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Name.ToLowerInvariant();
    }

    public override string ToString() => $"{Kind}/{Name}";
}
