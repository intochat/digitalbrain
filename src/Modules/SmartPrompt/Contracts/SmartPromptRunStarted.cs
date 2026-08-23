using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Messaging;

namespace DigitalBrain.SmartPrompt;

[GenerateSerializer]
[Alias("db.smart-prompt.run-started.v1")]
public sealed record SmartPromptRunStarted(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string PromptName,
    [property: Id(2)] ExecutionId ExecutionId) : Synapse;
