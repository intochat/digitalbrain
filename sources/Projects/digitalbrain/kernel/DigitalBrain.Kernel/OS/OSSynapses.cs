using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.OS;

[GenerateSerializer]
public sealed record BootSystem : Synapse;

[GenerateSerializer]
public sealed record DiscoverNeuronsRequest : Synapse;

[GenerateSerializer]
public sealed record InitializeGateway : Synapse;

[GenerateSerializer]
public sealed record InitializeGenesis(
    [property: Id(0)] string TopologyPath
) : Synapse;

[GenerateSerializer]
public sealed record ConfigureAiSubsystem(
    [property: Id(0)] string[] Providers,
    [property: Id(1)] string EmbeddingModel,
    [property: Id(2)] string VoiceModel
) : Synapse;
