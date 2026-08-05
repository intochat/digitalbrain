namespace DigitalBrain;

public readonly record struct NeuronId(string Kind, string Name)
{
    // The one minting convention. The boot catalog, journal entries, grain addresses and
    // test sugar all call this — one derivation, no second truth source.
    public static string KindOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type.Name.ToLowerInvariant();
    }

    public override string ToString() => $"{Kind}/{Name}";
}
