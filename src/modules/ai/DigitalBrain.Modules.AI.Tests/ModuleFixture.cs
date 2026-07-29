using DigitalBrain.AI;
using DigitalBrain.Testing;

namespace DigitalBrain.ModuleTests;

public sealed class ModuleFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<AIModule>();
        brain.ConfigureScriptedChat(
            typeof(DigitalBrain.AI.Ollama.Llama32),
            typeof(DigitalBrain.AI.Ollama.Gemma4));
    }
}
