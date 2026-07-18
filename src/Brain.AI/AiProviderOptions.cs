namespace DigitalBrain.AI;

public sealed class AiProviderOptions
{
    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int MaximumDiscussionSteps { get; set; } = 8;
}

public static class AiServiceKeys
{
    public const string Gpt56ChatClient = "gpt56";
    public const string Grok45ChatClient = "grok45";
}
