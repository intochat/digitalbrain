namespace DigitalBrain.Runtime.Ai;

public interface ILlmProvider
{
    static abstract string  Name { get; }
    static abstract string? SecretParameterName { get; }
    static abstract string? SecretDescription { get; }
}

public sealed class OpenAI : ILlmProvider
{
    public static string  Name => "openai";
    public static string? SecretParameterName => "openai-api-key";
    public static string? SecretDescription =>
        "Get your key at [platform.openai.com/api-keys](https://platform.openai.com/api-keys).";
}

public sealed class Anthropic : ILlmProvider
{
    public static string  Name => "anthropic";
    public static string? SecretParameterName => "anthropic-api-key";
    public static string? SecretDescription =>
        "Get your key at [console.anthropic.com/settings/keys](https://console.anthropic.com/settings/keys).";
}

public sealed class Ollama : ILlmProvider
{
    public static string  Name => "ollama";
    public static string? SecretParameterName => null;
    public static string? SecretDescription => null;
}

public sealed class Grok : ILlmProvider
{
    public static string  Name => "grok";
    public static string? SecretParameterName => "grok-api-key";
    public static string? SecretDescription =>
        "Get your key at [console.x.ai](https://console.x.ai/).";
}
