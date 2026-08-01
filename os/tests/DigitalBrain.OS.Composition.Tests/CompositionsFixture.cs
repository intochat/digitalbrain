using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Flutter;
using DigitalBrain.Memory;
using DigitalBrain.Testing;
using DigitalBrain.Time;

namespace DigitalBrain.Compositions.Tests;

public sealed class CompositionsFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<FlutterModule>();
        brain.AddModule<TimeModule>();
        brain.AddModule<AIModule>();
        brain.AddModule<MemoryModule>();
        brain.ConfigureScriptedChat(typeof(Llama32));
    }
}
