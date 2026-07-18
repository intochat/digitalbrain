using DigitalBrain;
using Xunit;

namespace DigitalBrain.Tests.AI;

public sealed class ModelDescriptorTests
{
    [Theory]
    [InlineData(typeof(GptFast), ModelProvider.OpenAI, ModelCapability.Chat)]
    [InlineData(typeof(ClaudeBalanced), ModelProvider.Anthropic, ModelCapability.Chat)]
    [InlineData(typeof(GptReasoning), ModelProvider.OpenAI, ModelCapability.Chat)]
    [InlineData(typeof(TextEmbedding), ModelProvider.OpenAI, ModelCapability.Embedding)]
    public void Descriptors_expose_provider_model_and_capability(
        Type descriptorType,
        ModelProvider expectedProvider,
        ModelCapability expectedCapability)
    {
        var descriptor = Assert.IsAssignableFrom<ModelDescriptor>(Activator.CreateInstance(descriptorType));

        Assert.Equal(expectedProvider, descriptor.Provider);
        Assert.Equal(expectedCapability, descriptor.Capability);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.ModelId));
    }

    [Fact]
    public void Descriptors_carry_no_secret_shaped_members()
    {
        foreach (var descriptorType in new[]
                 {
                     typeof(GptFast), typeof(ClaudeBalanced), typeof(GptReasoning), typeof(TextEmbedding),
                     typeof(ModelDescriptor), typeof(ChatModelDescriptor), typeof(EmbeddingModelDescriptor)
                 })
            foreach (var property in descriptorType.GetProperties())
            {
                Assert.DoesNotContain("Key", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Secret", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Token", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Credential", property.Name, StringComparison.OrdinalIgnoreCase);
            }
    }

    [Fact]
    public void Chat_and_embedding_descriptors_are_distinct_families()
    {
        Assert.True(typeof(ChatModelDescriptor).IsAssignableFrom(typeof(GptFast)));
        Assert.True(typeof(ChatModelDescriptor).IsAssignableFrom(typeof(ClaudeBalanced)));
        Assert.True(typeof(ChatModelDescriptor).IsAssignableFrom(typeof(GptReasoning)));
        Assert.True(typeof(EmbeddingModelDescriptor).IsAssignableFrom(typeof(TextEmbedding)));
        Assert.False(typeof(ChatModelDescriptor).IsAssignableFrom(typeof(TextEmbedding)));
        Assert.False(typeof(EmbeddingModelDescriptor).IsAssignableFrom(typeof(GptFast)));
    }

    [Fact]
    public void Role_clients_are_distinct_compile_time_types()
    {
        var roleClientTypes = new[]
        {
            typeof(FastConversationClient),
            typeof(BalancedConversationClient),
            typeof(ReasoningConversationClient)
        };

        Assert.Equal(roleClientTypes.Length, roleClientTypes.Distinct().Count());
        Assert.All(roleClientTypes, type => Assert.True(type.IsSealed));
        Assert.All(roleClientTypes, type => Assert.False(typeof(IAddressable).IsAssignableFrom(type)));
    }

    [Fact]
    public void Public_ai_and_conversation_sources_contain_no_keyed_provider_di_or_provider_sdk()
    {
        var sourceDirectories = new[]
        {
            RepositorySource.Directory("kernel", "DigitalBrain.Abstractions", "AI"),
            RepositorySource.Directory("kernel", "DigitalBrain.Abstractions", "Conversations"),
            RepositorySource.Directory("kernel", "DigitalBrain.Client", "AI"),
            RepositorySource.Directory("kernel", "DigitalBrain.Client", "Conversations")
        };

        foreach (var sources in sourceDirectories.Select(RepositorySource.ReadAll))
        {
            Assert.DoesNotContain("KeyedService", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("AddKeyed", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("FromKeyedServices", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("OpenAI.", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("Anthropic.", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("using OpenAI", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("using Anthropic", sources, StringComparison.Ordinal);
        }
    }
}

internal static class RepositorySource
{
    public static string Directory(params string[] relativeSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
                continue;

            return Path.Combine([directory.FullName, .. relativeSegments]);
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    public static string ReadAll(string directory) =>
        System.IO.Directory.Exists(directory)
            ? string.Join('\n', System.IO.Directory
                .GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText))
            : throw new DirectoryNotFoundException($"{directory} does not exist.");
}
