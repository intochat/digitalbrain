using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;

namespace DigitalBrain.ModuleTests;

public sealed class ModuleFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<AIModule>();
        brain.AddModule<TasksModule>();
        brain.AddModule<GoogleModule>();
        brain.AddModule<SalesforceModule>();
        brain.AddModule<ModuleDriverModule>();
        brain.ConfigureModuleEdges();
    }
}
