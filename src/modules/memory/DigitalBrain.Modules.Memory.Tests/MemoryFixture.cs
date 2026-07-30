using DigitalBrain.Memory;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Memory.Tests;

public sealed class MemoryFixture : DigitalBrainFixture
{
    public const string Memory = "memory";
    public const string OtherOwner = "other";

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<MemoryModule>();
        brain.ConfigureServiceEdge(
            static services => services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, DeterministicEmbeddingGenerator>(),
            new object(),
            static _ => { });
    }
}
