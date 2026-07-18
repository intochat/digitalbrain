using DigitalBrain.Protocol.Domain.Events;
namespace DigitalBrain.Os.Domain.Events;

[GenerateSerializer]
public sealed record GuideRequest(string? Section = null) : Synapse;

[GenerateSerializer]
public sealed record GuideNavigate(string Section) : Synapse;