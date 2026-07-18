using Ino.Core;
using Ino.Core.Hosting.Llm;

namespace Ino.Llm.Xai.Models;

public sealed class Grok4FastNonReasoning : LlmModel
{
    public override string Id => "grok-4-1-fast-non-reasoning";
    public override string DisplayName => "Grok 4.1 Fast (no reasoning)";
    public override string Provider => "xai";
    public override LlmTier DefaultTier => LlmTier.Fast;
}
