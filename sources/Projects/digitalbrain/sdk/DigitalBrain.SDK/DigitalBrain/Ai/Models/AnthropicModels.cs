namespace DigitalBrain.SDK.DigitalBrain.Ai.Models;

public sealed class Claude5Haiku : LlmModel
{
    public override string Id => "claude-5-haiku";
    public override string Provider => "anthropic";
    public override string DisplayName => "Claude 5 Haiku";
}

public sealed class Sonnet47 : LlmModel
{
    public override string Id => "claude-sonnet-4-7";
    public override string Provider => "anthropic";
    public override string DisplayName => "Claude Sonnet 4.7";
}

public sealed class Opus47 : LlmModel
{
    public override string Id => "claude-opus-4-7";
    public override string Provider => "anthropic";
    public override string DisplayName => "Claude Opus 4.7";
}
