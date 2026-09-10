using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

/// <summary>Creates a workspace or refreshes its title.</summary>
[GenerateSerializer]
[Alias("ui.ensure-workspace")]
public sealed record EnsureWorkspace(
    CommandId Id,
    [property: Id(0)] string Name,
    [property: Id(1)] string Title) : Command(Id);
