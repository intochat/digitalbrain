namespace DigitalBrain.Kernel.Capabilities;

[GrainType("digitalbrain.capability-catalog-projection")]
internal sealed class CapabilityCatalogProjectionGrain(ICapabilityCatalog catalog) : Grain, ICapabilityCatalogProjectionGrain
{
    private const int MaximumDescriptors = 256;

    public Task<CapabilityDescriptor[]> ReadAsync()
    {
        var snapshot = catalog.Snapshot()
            ?? throw new InvalidDataException("The capability catalog is unavailable.");
        if (snapshot.Count is 0 or > MaximumDescriptors)
            throw new InvalidDataException("The capability catalog exceeds its read bound.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var copy = new CapabilityDescriptor[snapshot.Count];
        for (var index = 0; index < snapshot.Count; index++)
        {
            var descriptor = snapshot[index]
                ?? throw new InvalidDataException("The capability catalog contains an invalid descriptor.");
            if (!Identifier(descriptor.Id, 256) || !ids.Add(descriptor.Id) ||
                descriptor.Version < 1 ||
                !Text(descriptor.Name, 256) ||
                !Text(descriptor.Description, 4096) ||
                !Enum.IsDefined(descriptor.Origin) ||
                !Enum.IsDefined(descriptor.Kind))
                throw new InvalidDataException("The capability catalog contains an invalid descriptor.");
            var examples = Values(descriptor.Examples, 32, 1024, identifiers: false);
            var grants = Values(descriptor.RequiredGrants, 32, 256, identifiers: false);
            var connections = Values(descriptor.RequiredConnections, 32, 256, identifiers: true);
            copy[index] = descriptor with
            {
                Examples = examples,
                RequiredGrants = grants,
                RequiredConnections = connections
            };
        }
        return Task.FromResult(copy.OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal).ToArray());
    }

    private static string[] Values(string[]? values, int maximumCount, int maximumLength, bool identifiers)
    {
        if (values is null || values.Length > maximumCount ||
            values.Any(value => identifiers ? !Identifier(value, maximumLength) : !Text(value, maximumLength)) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException("The capability catalog contains an invalid descriptor collection.");
        return values.ToArray();
    }

    private static bool Identifier(string? value, int maximumLength) =>
        Text(value, maximumLength) && value!.All(character =>
            char.IsLower(character) || char.IsDigit(character) || character is '.' or '-' or '_');

    private static bool Text(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) && !value.Any(char.IsControl);
}
