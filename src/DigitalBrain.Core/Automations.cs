namespace DigitalBrain.Core;

/// Lightweight reactive automations: data-driven reactions + small C# scripts.
/// This is the fast path for self-writing, "when X then Y", personal apps, and glue.
/// Complements (does not replace) full NeuroPack bundles for rich/marketplace content.
/// All definitions are journaled and hot-activatable.

[GenerateSerializer]
[Alias("DigitalBrain.Core.RegisterScript")]
public record RegisterScript(
    [property: Id(0)] string Id,
    [property: Id(1)] string Code,
    [property: Id(2)] string Description = "",
    [property: Id(3)] IReadOnlyList<string> DeclaredEmits = null!,
    [property: Id(4)] string Scope = "default")
    : Synapse(nameof(RegisterScript), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.RegisterReaction")]
public record RegisterReaction(
    [property: Id(0)] string Id,
    [property: Id(1)] string When,
    [property: Id(2)] string ScriptRef,
    [property: Id(3)] string? Target = null,
    [property: Id(4)] IReadOnlyList<string> DeclaredEmits = null!,
    [property: Id(5)] string Scope = "default")
    : Synapse(nameof(RegisterReaction), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.AutomationApp")]
public record AutomationApp(
    [property: Id(0)] string AppId,
    [property: Id(1)] string Description = "",
    [property: Id(2)] IReadOnlyList<RegisterScript> Scripts = null!,
    [property: Id(3)] IReadOnlyList<RegisterReaction> Reactions = null!)
    : Synapse(nameof(AutomationApp), DateTimeOffset.UtcNow);

/// Convenience record for higher-level creation (e.g. from Ino or UI).
/// The grain will expand it into Register* entries.
[GenerateSerializer]
[Alias("DigitalBrain.Core.CreateAutomationApp")]
public record CreateAutomationApp(
    [property: Id(0)] string AppId,
    [property: Id(1)] string Description = "",
    [property: Id(2)] IReadOnlyList<RegisterScript>? Scripts = null,
    [property: Id(3)] IReadOnlyList<RegisterReaction>? Reactions = null)
    : Synapse(nameof(CreateAutomationApp), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.AutomationDefinitionStaged")]
public record AutomationDefinitionStaged(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string AutomationNeuronId,
    [property: Id(2)] RegisterScript Script,
    [property: Id(3)] RegisterReaction Reaction)
    : Synapse(nameof(AutomationDefinitionStaged), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.RemoveReaction")]
public record RemoveReaction(string Id) : Synapse(nameof(RemoveReaction), DateTimeOffset.UtcNow);

/// Thin promotion bridge (priority 6): take reactions/scripts and emit a seed that pack pipeline can consume.
/// Does not replace full authoring; just crystallizes the lightweight def into NeuroPack form for distribution.
[GenerateSerializer]
[Alias("DigitalBrain.Core.PromoteAutomationToPack")]
public record PromoteAutomationToPack(
    [property: Id(0)] string PackName,
    [property: Id(1)] string Version,
    [property: Id(2)] IReadOnlyList<string> ReactionIds,
    [property: Id(3)] string? OwnerId = null)
    : Synapse(nameof(PromoteAutomationToPack), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.AutomationPromoted")]
public record AutomationPromoted(string PackName, string Version, string ManifestSummary) : Synapse(nameof(AutomationPromoted), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.IAutomationNeuron")]
public interface IAutomationNeuron : INeuron
{
    [Alias("ListActiveScriptsAsync")]
    Task<IReadOnlyList<string>> ListActiveScriptsAsync();
    [Alias("ListActiveReactionsAsync")]
    Task<IReadOnlyList<string>> ListActiveReactionsAsync();

    /// Trusted/bootstrap convenience: define a reaction + inline script body in one call.
    /// User/MCP-created executable C# must stage AutomationDefinitionStaged through the self-evolution rail first.
    [Alias("DefineReactionAsync")]
    Task DefineReactionAsync(string id, string when, string? target, string scriptCode, IReadOnlyList<string>? declaredEmits = null);

    /// Get script source by id for library/reuse (documented for surfaces + MCP).
    [Alias("GetScriptCodeAsync")]
    Task<string?> GetScriptCodeAsync(string id);
    [Alias("RemoveReactionAsync")]
    Task RemoveReactionAsync(string id);

    /// Richer library view for MCP/UI (description, declared emits, usage). Surfaces (AutomationSurface, AutomationGraphSurface) emitted on changes/queries.
    [Alias("ListScriptLibraryAsync")]
    Task<IReadOnlyList<ScriptLibraryEntry>> ListScriptLibraryAsync();

    /// Promote selected reactions to NeuroPack seed (thin bridge to heavy rail).
    [Alias("PromoteToPackAsync")]
    Task PromoteToPackAsync(string packName, string version, IReadOnlyList<string> reactionIds, string? ownerId = null);
}

[GenerateSerializer]
[Alias("DigitalBrain.Core.ScriptLibraryEntry")]
public record ScriptLibraryEntry(
    [property: Id(0)] string Id,
    [property: Id(1)] string Code,
    [property: Id(2)] string Description,
    [property: Id(3)] IReadOnlyList<string> DeclaredEmits,
    [property: Id(4)] int UsageCount
);

