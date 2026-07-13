namespace DigitalBrain.Core;

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
    [property: Id(5)] string Scope = "default",
    [property: Id(6)] string? Schedule = null)
    : Synapse(nameof(RegisterReaction), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.AutomationApp")]
public record AutomationApp(
    [property: Id(0)] string AppId,
    [property: Id(1)] string Description = "",
    [property: Id(2)] IReadOnlyList<RegisterScript> Scripts = null!,
    [property: Id(3)] IReadOnlyList<RegisterReaction> Reactions = null!)
    : Synapse(nameof(AutomationApp), DateTimeOffset.UtcNow);

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
[Alias("DigitalBrain.Core.AutomationRemovalStaged")]
public record AutomationRemovalStaged(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string AutomationNeuronId,
    [property: Id(2)] string ReactionId)
    : Synapse(nameof(AutomationRemovalStaged), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.RemoveReaction")]
public record RemoveReaction(string Id) : Synapse(nameof(RemoveReaction), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.IAutomationNeuron")]
public interface IAutomationNeuron : INeuron
{
    [Alias("ListActiveScriptsAsync")]
    Task<IReadOnlyList<string>> ListActiveScriptsAsync();
    [Alias("ListActiveReactionsAsync")]
    Task<IReadOnlyList<string>> ListActiveReactionsAsync();

    [Alias("DefineReactionAsync")]
    Task DefineReactionAsync(string id, string when, string? target, string scriptCode, IReadOnlyList<string>? declaredEmits = null, CancellationToken cancellationToken = default);

    [Alias("GetScriptCodeAsync")]
    Task<string?> GetScriptCodeAsync(string id);
    [Alias("RemoveReactionAsync")]
    Task RemoveReactionAsync(string id);

    [Alias("ListScriptLibraryAsync")]
    Task<IReadOnlyList<ScriptLibraryEntry>> ListScriptLibraryAsync();

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

