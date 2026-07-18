namespace Brain.Contracts;

[GenerateSerializer, Alias("brain.neuron-address.v1")]
public readonly record struct NeuronAddress(
    [property: Id(0)] OrganizationId OrganizationId,
    [property: Id(1)] SpaceId SpaceId,
    [property: Id(2)] string ContractId,
    [property: Id(3)] string InstanceId)
{
    public string ToGrainKey() => $"{OrganizationId.Value}|{SpaceId.Value}|{ContractId}/{InstanceId}";

    public static NeuronAddress Parse(string grainKey)
    {
        var parts = grainKey.Split('|');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Invalid neuron grain key '{grainKey}'.", nameof(grainKey));

        var slash = parts[2].IndexOf('/');
        if (slash <= 0 || slash == parts[2].Length - 1)
            throw new ArgumentException($"Invalid neuron grain key '{grainKey}'.", nameof(grainKey));

        return new NeuronAddress(
            new OrganizationId(parts[0]),
            new SpaceId(parts[1]),
            parts[2][..slash],
            parts[2][(slash + 1)..]);
    }
}
