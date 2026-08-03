using DigitalBrain.Memory.Qdrant;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Memory.Tests;

public sealed class MemoryModuleProviderMisconfiguration
{
    [Fact(DisplayName = "silo startup refuses a configured Qdrant connection when the provider setting does not select Qdrant")]
    public async Task ConfiguredConnectionWithoutQdrantProviderFailsFast()
    {
        await using var fixture = new MisconfiguredQdrantConnectionFixture();

        var exception = await Record.ExceptionAsync(async () => await fixture.InitializeAsync());

        Assert.NotNull(exception);
        var flattened = FlattenMessages(exception!);
        Assert.Contains(QdrantVectorMemoryRegistration.DefaultConnectionName, flattened, StringComparison.Ordinal);
        Assert.Contains(MemoryModule.ProviderConfigurationKey, flattened, StringComparison.Ordinal);
    }

    private static string FlattenMessages(Exception exception)
    {
        var messages = new List<string>();
        Visit(exception, messages);
        return string.Join('\n', messages);

        static void Visit(Exception current, List<string> messages)
        {
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    Visit(inner, messages);
                }
            }
            else if (current.InnerException is { } innerException)
            {
                Visit(innerException, messages);
            }
        }
    }
}

internal sealed class MisconfiguredQdrantConnectionFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.Configure(
            $"ConnectionStrings:{QdrantVectorMemoryRegistration.DefaultConnectionName}",
            "Endpoint=http://127.0.0.1:6334");
        brain.AddModule<MemoryModule>();
        brain.ConfigureServiceEdge(
            static services => services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, DeterministicEmbeddingGenerator>(),
            new object(),
            static _ => { });
    }
}
