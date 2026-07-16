namespace Brain.Contracts;

[GenerateSerializer, Alias("brain.neuron-address.v2")]
public readonly record struct NeuronAddress(
    [property: Id(0)] string OwnerId,
    [property: Id(1)] string SpaceId,
    [property: Id(2)] string NeuronId)
{
    public string ToGrainKey() => $"{OwnerId}|{SpaceId}|{NeuronId}";
    public string Kind => NeuronId[..(NeuronId.IndexOf('/', StringComparison.Ordinal) switch { < 0 => NeuronId.Length, var i => i })];

    public static NeuronAddress Parse(string grainKey)
    {
        var parts = grainKey.Split('|');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Invalid neuron grain key '{grainKey}'.", nameof(grainKey));
        return new NeuronAddress(parts[0], parts[1], parts[2]);
    }
}
