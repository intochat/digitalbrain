namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.model-tier")]
public enum ModelTier
{
    Fast,
    Balanced,
    Reasoning,
}
