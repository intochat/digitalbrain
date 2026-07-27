using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Chat;
using DigitalBrain.Flutter;
using DigitalBrain.OS;
using DigitalBrain.Testing;

namespace DigitalBrain.OS.Bdd.Tests;

public sealed class OSFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<FlutterModule>();
        brain.AddModule<ChatModule>();
        brain.AddModule<AIModule>();
        brain.AddModule<OSBehaviorsModule>();
        brain.ConfigureScriptedChat(typeof(Llama32));
    }
}
