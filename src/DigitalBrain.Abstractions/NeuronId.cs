namespace DigitalBrain;

public readonly record struct NeuronId(string Kind, string Name)
{
    public static string KindOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        foreach (var attribute in type.CustomAttributes)
        {
            if (attribute.AttributeType.FullName == "Orleans.GrainTypeAttribute"
                && attribute.ConstructorArguments.Count == 1
                && attribute.ConstructorArguments[0].Value is string kind
                && !string.IsNullOrWhiteSpace(kind))
            {
                return kind;
            }
        }

        // Stage-1 fact convention only; Hosting rejects neurons without [GrainType("stable-kind")].
        return type.Name.ToLowerInvariant();
    }

    public override string ToString() => $"{Kind}/{Name}";
}
