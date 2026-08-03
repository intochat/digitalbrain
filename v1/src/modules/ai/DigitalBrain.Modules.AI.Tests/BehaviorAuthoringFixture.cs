using DigitalBrain.AI;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Testing;

namespace DigitalBrain.ModuleTests;

// Deliberately registers no IBehaviorAuthor: BehaviorAuthorNeuron.Author() must then resolve
// through its DI-fallback lambda over IGemma4, the production path that ModuleFixture's model-only
// setup never exercises and UiEdgeFixture always bypasses by scripting IBehaviorAuthor directly.
public sealed class BehaviorAuthoringFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<AIModule>();
        brain.AddModule<BehaviorsModule>();
    }
}
