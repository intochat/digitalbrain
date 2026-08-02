using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Chat;
using DigitalBrain.Memory;
using DigitalBrain.OS;
using DigitalBrain.Shell;
using DigitalBrain.Testing;
using DigitalBrain.Time;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.OS.Bdd.Tests;

public sealed class OSFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<ShellModule>();
        brain.AddModule<ChatModule>();
        brain.AddModule<AIModule>();
        brain.AddModule<MemoryModule>();
        brain.AddModule<TimeModule>();
        brain.AddModule<OSBehaviorsModule>();
        brain.ConfigureScriptedChat(typeof(Gemma4), typeof(Llama32));
        brain.ConfigureServiceEdge(
            static services => services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, ScriptedEmbeddingGenerator>(),
            new object(),
            static _ => { });
    }
}
