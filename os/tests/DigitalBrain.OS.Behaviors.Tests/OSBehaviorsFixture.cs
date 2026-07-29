using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Chat;
using DigitalBrain.OS;
using DigitalBrain.Testing;

namespace DigitalBrain.OS.Behaviors.Tests;

public sealed class OSBehaviorsFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<ChatModule>();
        brain.AddModule<AIModule>();
        brain.AddModule<OSBehaviorsModule>();
        brain.ConfigureScriptedChat(
            typeof(Gemma4),
            typeof(Llama32),
            typeof(Qwen35),
            typeof(Granite41));
    }
}
