using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Chat;
using DigitalBrain.Introspection;
using DigitalBrain.OS.Assistant;
using DigitalBrain.Testing;

namespace DigitalBrain.OS.Assistant.Tests;

public sealed class OSBehaviorsFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<ChatModule>();
        brain.AddModule<AIModule>();
        brain.AddModule<AssistantModule>();
        brain.AddModule<IntrospectionModule>();
        brain.ConfigureScriptedChat(
            typeof(Gemma4),
            typeof(Llama32),
            typeof(Qwen35),
            typeof(Granite41));
    }
}
