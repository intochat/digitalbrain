using DigitalBrain.Kernel.Memory;
namespace DigitalBrain.Kernel.Capabilities;

internal sealed class BuiltInCapabilityCatalog : ICapabilityCatalog
{
    internal const string AssistantAnswerCapabilityId = "assistant.answer";
    private readonly IReadOnlyDictionary<string, CapabilityDescriptor> _descriptorsById;
    private readonly IReadOnlyList<CapabilityDescriptor> _orderedDescriptors;
    public BuiltInCapabilityCatalog(IEnumerable<ICapabilityDescriptorSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var descriptorsById = new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in PlatformDescriptors())
            Register(descriptorsById, descriptor);
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            foreach (var descriptor in source.Descriptors)
                Register(descriptorsById, descriptor);
        }
        _descriptorsById = descriptorsById;
        _orderedDescriptors = descriptorsById.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }
    public IReadOnlyList<CapabilityDescriptor> Snapshot() => _orderedDescriptors;
    public bool TryBind(string capabilityId, out CapabilityDescriptor descriptor) =>
        _descriptorsById.TryGetValue(capabilityId, out descriptor!);
    private static void Register(IDictionary<string, CapabilityDescriptor> descriptors, CapabilityDescriptor descriptor)
    {
        if (!descriptors.TryAdd(descriptor.Id, descriptor))
            throw new InvalidOperationException($"Capability descriptor '{descriptor.Id}' is registered more than once.");
    }
    private static IEnumerable<CapabilityDescriptor> PlatformDescriptors()
    {
        yield return new CapabilityDescriptor(
            AssistantAnswerCapabilityId,
            1,
            "Assistant answer",
            "Answers the user directly from the assistant's own reasoning without reading or changing any external system.",
            ["Explain what a capability grant is.", "What time zone is Kyiv in?", "what can you do"],
            [],
            [],
            CapabilityOrigin.Platform,
            CapabilityOperationKind.Query,
            true);
        yield return new CapabilityDescriptor(
            MemoryCapabilityIds.Recall,
            1,
            "Recall remembered facts",
            "Searches facts the user previously asked the brain to remember.",
            ["What did I say my shirt size was?", "Do you remember my home Wi-Fi name?"],
            [],
            [],
            CapabilityOrigin.Platform,
            CapabilityOperationKind.Query,
            true);
        yield return new CapabilityDescriptor(
            MemoryCapabilityIds.Remember,
            1,
            "Remember a fact",
            "Stores a fact in the user's private memory for later recall.",
            ["Remember that my passport expires in March 2027.", "Note that Anna prefers morning meetings."],
            [],
            [],
            CapabilityOrigin.Platform,
            CapabilityOperationKind.InternalWrite,
            true);
    }
}
