using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// Modules contribute AI tools without the AI module referencing them (UI → AI is
// the allowed direction; this seam inverts tool ownership).
public interface IAgentToolSource
{
    IReadOnlyList<AIFunction> ToolsFor(OwnerId owner) => [];

    // Existing connector sources need only an owner; delegation sources receive an
    // actual sender bound to this model turn instead of re-entering the owner root.
    IReadOnlyList<AIFunction> ToolsFor(AgentToolContext context) => ToolsFor(context.Owner);
}
