using DigitalBrain.Abstractions.Behavior;
using DigitalBrain.Core.Behavior;

namespace IntoChat;

internal sealed class IntoChatBehaviorDefaults : IBehaviorDefaults
{
    public IReadOnlyList<BehaviorDefinition> Definitions { get; } =
        [BehaviorExamples.IntoChat, .. BehaviorExamples.All];
}
