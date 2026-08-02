using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using DigitalBrain.Behaviors.Runtime;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorsFixture : DigitalBrainFixture
{
    public const string SampleBehavior = "com.digitalbrain.sample";
    public const string AccountEnrichmentBehavior = "com.digitalbrain.account-enrichment";

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<BehaviorsModule>();
        brain.AddModule<TasksModule>();
    }
}
