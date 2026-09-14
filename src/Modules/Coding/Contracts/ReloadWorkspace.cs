using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.reload-workspace")]
public sealed record ReloadWorkspace(CommandId Id) : Command(Id);
