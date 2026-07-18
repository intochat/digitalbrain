using Ino.Core;
using Ino.Core.Hosting.Llm;

namespace Ino.Llm.Xai.Models;

public sealed class Grok420 : LlmModel
{
    public override string Id => "grok-4.20";
    public override string DisplayName => "Grok 4.20 (flagship)";
    public override string Provider => "xai";
    public override LlmTier DefaultTier => LlmTier.Reasoning;
}
