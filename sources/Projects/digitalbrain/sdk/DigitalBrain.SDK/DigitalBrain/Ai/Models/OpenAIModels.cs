using DigitalBrain.Runtime.Ai;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Models;

public sealed class Gpt5Nano : LlmModel
{
    public override string Id => "gpt-5-nano";
    public override string Provider => "openai";
    public override string DisplayName => "GPT-5 Nano";
}

public sealed class Gpt5Mini : LlmModel
{
    public override string Id => "gpt-5-mini";
    public override string Provider => "openai";
    public override string DisplayName => "GPT-5 Mini";
}

public sealed class Gpt5 : LlmModel
{
    public override string Id => "gpt-5";
    public override string Provider => "openai";
    public override string DisplayName => "GPT-5";
}

public sealed class TextEmbedding3Small : EmbeddingModel
{
    public override string Id => "text-embedding-3-small";
    public override string Provider => "openai";
    public override string DisplayName => "Text Embedding 3 Small";
    public override int Dimensions => 1536;
}

public sealed class TextEmbedding3Large : EmbeddingModel
{
    public override string Id => "text-embedding-3-large";
    public override string Provider => "openai";
    public override string DisplayName => "Text Embedding 3 Large";
    public override int Dimensions => 3072;
}
