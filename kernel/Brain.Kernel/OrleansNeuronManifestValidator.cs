using Orleans.Metadata;
using Orleans.Runtime;

namespace Brain.Kernel;

public static class OrleansNeuronManifestValidator
{
    public static void Validate(
        IEnumerable<NeuronRegistration> registrations,
        GrainManifest manifest)
    {
        foreach (var registration in registrations
            .OrderBy(item => item.Contract.FullName, StringComparer.Ordinal)
            .ThenBy(item => item.Implementation.FullName, StringComparer.Ordinal))
        {
            if (!IsPresentInManifest(registration, manifest))
            {
                throw new InvalidOperationException(
                    $"Neuron registration for contract '{registration.Contract.FullName}' and implementation '{registration.Implementation.FullName}' is absent from the Orleans local grain manifest.");
            }
        }
    }

    private static bool IsPresentInManifest(
        NeuronRegistration registration,
        GrainManifest manifest)
    {
        var implementationFullName = registration.Implementation.FullName;
        if (implementationFullName is null)
            return false;

        GrainType? matchedGrainType = null;
        foreach (var (grainType, grainProperties) in manifest.Grains
            .OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal))
        {
            if (grainProperties.Properties.TryGetValue(
                    WellKnownGrainTypeProperties.FullTypeName,
                    out var fullTypeName)
                && string.Equals(fullTypeName, implementationFullName, StringComparison.Ordinal))
            {
                matchedGrainType = grainType;
                break;
            }
        }

        if (matchedGrainType is null)
            return false;

        var grainTypeValue = matchedGrainType.Value.ToString();
        var grainPropertiesForMatch = manifest.Grains[matchedGrainType.Value];
        var contractName = registration.Contract.Name;
        var contractFullName = registration.Contract.FullName;

        foreach (var (interfaceType, interfaceProperties) in manifest.Interfaces
            .OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal))
        {
            if (!interfaceProperties.Properties.TryGetValue(
                    WellKnownGrainInterfaceProperties.TypeName,
                    out var typeName))
            {
                continue;
            }

            var nameMatches =
                string.Equals(typeName, contractName, StringComparison.Ordinal)
                || string.Equals(typeName, contractFullName, StringComparison.Ordinal);
            if (!nameMatches)
                continue;

            var defaultGrainTypeMatches =
                interfaceProperties.Properties.TryGetValue(
                    WellKnownGrainInterfaceProperties.DefaultGrainType,
                    out var defaultGrainType)
                && string.Equals(defaultGrainType, grainTypeValue, StringComparison.Ordinal);

            var implementedInterfaceMatches =
                GrainListsImplementedInterface(grainPropertiesForMatch, interfaceType);

            if (defaultGrainTypeMatches || implementedInterfaceMatches)
                return true;
        }

        return false;
    }

    private static bool GrainListsImplementedInterface(
        GrainProperties grainProperties,
        GrainInterfaceType interfaceType)
    {
        var interfaceId = interfaceType.ToString();
        foreach (var (key, value) in grainProperties.Properties)
        {
            if (!key.StartsWith(
                    WellKnownGrainTypeProperties.ImplementedInterfacePrefix,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(value, interfaceId, StringComparison.Ordinal)
                || string.Equals(
                    key,
                    WellKnownGrainTypeProperties.ImplementedInterfacePrefix + interfaceId,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
