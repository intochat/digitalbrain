namespace DigitalBrain;

internal static class SynapseKinds
{
    internal static string NameOf(Type synapseType)
    {
        ArgumentNullException.ThrowIfNull(synapseType);
        return synapseType.FullName
            ?? throw new InvalidOperationException(
                $"{synapseType.Name} has no C# full name and cannot be a synapse kind.");
    }
}
