using System.Text.Json.Serialization;

namespace DigitalBrain.InoLang.Domain.Ino;

// Pure value objects for .ino rule AST (JSON + text roundtrip target). Explicit [Id] for Orleans/ser.
[GenerateSerializer]
public sealed record InoExperience(
    [property: Id(0)] string Name,
    [property: Id(1)] string Version,
    [property: Id(2)] string? Description,
    [property: Id(3)] string[] Emits,
    [property: Id(4)] RuleDeclaration[] Rules,
    [property: Id(5)] bool HasEscalateCodegen = false,
    [property: Id(6)] string? DefaultRegion = null,
    [property: Id(7)] bool DefaultPinned = false,
    [property: Id(8)] int DefaultOrder = 0,
    [property: Id(9)] string[] Requires = null,
    [property: Id(10)] bool IsSystem = false,
    [property: Id(11)] string[] RequiresGrant = null);

[GenerateSerializer]
public sealed record RuleDeclaration(
    [property: Id(0)] string On,
    [property: Id(1)] string? Alias,
    [property: Id(2)] RuleCondition? When,
    [property: Id(3)] RuleStatement[] Do);

[GenerateSerializer]
public sealed record RuleCondition(
    [property: Id(0)] string Field,
    [property: Id(1)] string Op,
    [property: Id(2)] string Value);

[GenerateSerializer]
public abstract record RuleStatement;

[GenerateSerializer]
public sealed record EmitRuleStatement([property: Id(0)] EmitDescriptor Emit) : RuleStatement;

[GenerateSerializer]
public sealed record ShowCardRuleStatement(
    [property: Id(0)] string? Title,
    [property: Id(1)] CardItem[] Items) : RuleStatement;

[GenerateSerializer]
public sealed record EmitDescriptor(
    [property: Id(0)] string SynapseType,
    [property: Id(1)] Dictionary<string, string> Args);

[GenerateSerializer]
public sealed record CardItem(
    [property: Id(0)] string Kind, // "text", "button", "column", "row"
    [property: Id(1)] string Text,
    [property: Id(2)] EmitDescriptor? Action = null,
    [property: Id(3)] CardItem[]? Children = null);

[GenerateSerializer]
public sealed record InoDiagnostic(
    [property: Id(0)] string Code, // INO001 etc
    [property: Id(1)] string Severity, // Error or Warning
    [property: Id(2)] int Line,
    [property: Id(3)] string Message);

[GenerateSerializer]
public sealed record RuleSet(
    [property: Id(0)] RuleDeclaration[] Declarations,
    [property: Id(1)] string[] Emits);

public sealed record BootManifest(
    string Name,
    string Version,
    string? Description,
    List<(string Model, string Tier)> Llms,
    string? Voice,
    string? Durability,
    string? Ui,
    bool Discovery,
    string? AdvertisedIpEnv,
    string[] Seeds,
    List<(string Name, string Path)> Worlds);
