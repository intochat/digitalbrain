namespace DigitalBrain.Kernel.Contracts.Models;

public abstract class DigitalBrainModel
{
    public abstract DigitalBrainCapabilityKind Kind { get; }
    public abstract string Provider { get; }
    public abstract string Id { get; }
    public virtual string DisplayName => Id;
    public virtual DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.FullyCapable;
    public DigitalBrainModelDescriptor Describe() => new(Kind, Provider, Id, DisplayName, Capabilities);
}
public abstract class LlmModel : DigitalBrainModel
{
    public sealed override DigitalBrainCapabilityKind Kind => DigitalBrainCapabilityKind.LargeLanguageModel;
}
public abstract class EmbeddingModel : DigitalBrainModel
{
    public sealed override DigitalBrainCapabilityKind Kind => DigitalBrainCapabilityKind.Embedding;
}
