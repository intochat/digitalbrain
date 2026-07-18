using DigitalBrain.SDK.DigitalBrain.Ai.Models;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Models.XAI.Grok;

public sealed class GrokBeta : LlmModel
{
    public override string Id => "grok-4.3";
    public override string Provider => "grok";
    public override string DisplayName => "Grok Beta";
}

public sealed class Grok2 : LlmModel
{
    public override string Id => "grok-4.3";
    public override string Provider => "grok";
    public override string DisplayName => "Grok 2";
}
