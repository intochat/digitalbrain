using Ino.Core;
using Ino.Core.Hosting.Llm;

namespace Ino.Llm.Xai.Models;

public sealed class Grok4FastReasoning : LlmModel
{
    public override string Id => "grok-4-1-fast-reasoning";
    public override string DisplayName => "Grok 4.1 Fast (reasoning)";
    public override string Provider => "xai";
    public override LlmTier DefaultTier => LlmTier.Balanced;
}
