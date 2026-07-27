using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Chat;
using DigitalBrain.Flutter;
using DigitalBrain.Os;
using DigitalBrain.Testing;

namespace DigitalBrain.Os.Bdd.Tests;

public sealed class OsFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<FlutterModule>();
        brain.AddModule<ChatModule>();
        brain.AddModule<AIModule>();
        brain.AddModule<OsBehaviorsModule>();
        brain.ConfigureScriptedChat(typeof(Llama32));
    }
}
