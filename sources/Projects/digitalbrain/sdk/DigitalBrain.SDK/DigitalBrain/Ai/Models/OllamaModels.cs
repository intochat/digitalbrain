using DigitalBrain.Runtime.Ai;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Models;

public sealed class Llama32_3B : LlmModel
{
    public override string Id => "llama3.2:3b";
    public override string Provider => "ollama";
    public override string DisplayName => "Llama 3.2 3B";
}

public sealed class Qwen25_7B : LlmModel
{
    public override string Id => "qwen2.5:7b";
    public override string Provider => "ollama";
    public override string DisplayName => "Qwen 2.5 7B";
}

public sealed class MxbaiEmbedLarge : EmbeddingModel
{
    public override string Id => "mxbai-embed-large";
    public override string Provider => "ollama";
    public override string DisplayName => "Mxbai Embed Large";
    public override int Dimensions => 1024;
}

public sealed class Qwen25_14B : LlmModel
{
    public override string Id => "qwen2.5:14b";
    public override string Provider => "ollama";
    public override string DisplayName => "Qwen 2.5 14B";
}

public sealed class Phi4 : LlmModel
{
    public override string Id => "phi4";
    public override string Provider => "ollama";
    public override string DisplayName => "Phi 4";
}

public sealed class NemotronMini : LlmModel
{
    public override string Id => "nemotron-mini";
    public override string Provider => "ollama";
    public override string DisplayName => "Nemotron Mini 4B";
}
