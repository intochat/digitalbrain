namespace DigitalBrain.Core;

/// Lightweight reactive automations: data-driven reactions + small C# scripts.
/// This is the fast path for self-writing, "when X then Y", personal apps, and glue.
/// Complements (does not replace) full NeuroPack bundles for rich/marketplace content.
/// All definitions are journaled and hot-activatable.

[GenerateSerializer]
public record RegisterScript(
    [property: Id(0)] string Id,
    [property: Id(1)] string Code,
    [property: Id(2)] string Description = "",
    [property: Id(3)] IReadOnlyList<string> DeclaredEmits = null!)
    : Synapse(nameof(RegisterScript), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record RegisterReaction(
    [property: Id(0)] string Id,
    [property: Id(1)] string When,
    [property: Id(2)] string ScriptRef,
    [property: Id(3)] string? Target = null,
    [property: Id(4)] IReadOnlyList<string> DeclaredEmits = null!)
    : Synapse(nameof(RegisterReaction), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record AutomationApp(
    [property: Id(0)] string AppId,
    [property: Id(1)] string Description = "",
    [property: Id(2)] IReadOnlyList<RegisterScript> Scripts = null!,
    [property: Id(3)] IReadOnlyList<RegisterReaction> Reactions = null!)
    : Synapse(nameof(AutomationApp), DateTimeOffset.UtcNow);

/// Convenience record for higher-level creation (e.g. from Ino or UI).
/// The grain will expand it into Register* entries.
[GenerateSerializer]
public record CreateAutomationApp(
    [property: Id(0)] string AppId,
    [property: Id(1)] string Description = "",
    [property: Id(2)] IReadOnlyList<RegisterScript>? Scripts = null,
    [property: Id(3)] IReadOnlyList<RegisterReaction>? Reactions = null)
    : Synapse(nameof(CreateAutomationApp), DateTimeOffset.UtcNow);

public interface IAutomationNeuron : INeuron
{
    Task<IReadOnlyList<string>> ListActiveScriptsAsync();
    Task<IReadOnlyList<string>> ListActiveReactionsAsync();
}