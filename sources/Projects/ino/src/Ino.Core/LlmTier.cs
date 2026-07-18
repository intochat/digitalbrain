namespace Ino.Core;

/// <summary>
/// Declarative quality tier requested for an LLM capability. Neurons resolve
/// an IChatClient for the tier they need via IChatClientFactory.ForTier.
/// If a tier is unbound by the AppHost, the factory falls back to the
/// highest-bound tier ≤ the requested tier (Reasoning > Balanced > Fast).
/// </summary>
public enum LlmTier
{
    None,
    Fast,
    Balanced,
    Reasoning,
}
