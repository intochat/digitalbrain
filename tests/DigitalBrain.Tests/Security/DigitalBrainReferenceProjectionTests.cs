using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.DigitalBrain;
using DigitalBrain;
using DigitalBrain.Tests.Aspire;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Security;

public sealed class DigitalBrainReferenceProjectionTests
{
    private static readonly string[] ClientMetadataKeys =
    [
        "DigitalBrain__Client__Name",
        "DigitalBrain__Client__ContractVersion"
    ];

    [Fact]
    public async Task Privileged_reference_contains_durability_and_provider_configuration()
    {
        var builder = AspireModelTestBuilder.Create();
        var brain = CompleteBrain(builder);
        var kernel = builder.AddContainer("kernel", "scratch")
            .WithReference(brain);

        var result = await ExecutionConfigurationAsync(kernel.Resource);
        var environment = result.EnvironmentVariables.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        Assert.Contains(environment.Keys, key => key.StartsWith("Orleans__Clustering__", StringComparison.Ordinal));
        Assert.Contains(environment.Keys, key => key.StartsWith("Orleans__Reminders__", StringComparison.Ordinal));
        Assert.Contains(environment.Keys, key => key.StartsWith("Orleans__GrainStorage__Default__", StringComparison.Ordinal));
        Assert.Contains(environment.Keys, key => key.StartsWith("Orleans__Streaming__NeuronNotification__", StringComparison.Ordinal));
        Assert.Contains("ConnectionStrings__journal", environment.Keys);
        Assert.Contains("DigitalBrain__Storage__Journal", environment.Keys);
        Assert.Contains("DigitalBrain__Storage__Outbox", environment.Keys);
        Assert.Contains("DigitalBrain__AI__OpenAI__ApiKey", environment.Keys);
        Assert.Contains("DigitalBrain__AI__OpenAI__Endpoint", environment.Keys);
        Assert.Contains("DigitalBrain__AI__OpenAI__FastModelId", environment.Keys);
        Assert.Contains("DigitalBrain__AI__OpenAI__ReasoningModelId", environment.Keys);
        Assert.Contains("DigitalBrain__AI__OpenAI__EmbeddingModelId", environment.Keys);
        Assert.Contains("DigitalBrain__AI__Anthropic__ApiKey", environment.Keys);
        Assert.Contains("DigitalBrain__AI__Anthropic__Endpoint", environment.Keys);
        Assert.Contains("DigitalBrain__AI__Anthropic__BalancedModelId", environment.Keys);
    }

    [Fact]
    public async Task Client_reference_contains_only_clustering_discovery_and_safe_metadata()
    {
        var builder = AspireModelTestBuilder.Create();
        var brain = CompleteBrain(builder);
        var client = builder.AddContainer("client", "scratch")
            .WithReference(brain.AsClient());

        var result = await ExecutionConfigurationAsync(client.Resource);
        var environment = result.EnvironmentVariables.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        Assert.NotEmpty(environment);
        Assert.Contains(environment.Keys, key => key.StartsWith("Orleans__Clustering__", StringComparison.Ordinal));
        Assert.All(environment.Keys, key =>
            Assert.True(
                key.StartsWith("Orleans__Clustering__", StringComparison.Ordinal) ||
                key == "ConnectionStrings__brain-clustering" ||
                key is "Orleans__ClusterId" or "Orleans__ServiceId" ||
                ClientMetadataKeys.Contains(key, StringComparer.Ordinal),
                $"Unexpected restricted client environment key: {key}"));
        Assert.DoesNotContain(environment.Keys, key =>
            key.Contains("Streaming", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Reminder", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("GrainStorage", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Journal", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Outbox", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Anthropic", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
        var references = FlattenReferences(result.References).ToArray();
        Assert.DoesNotContain(references, value => value is ParameterResource { Secret: true });
        Assert.Equal(
            ["brain-clustering"],
            references
                .OfType<ConnectionStringReference>()
                .Select(reference => reference.Resource.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.DoesNotContain(
            references.OfType<IResource>(),
            resource => resource.Name is not ("brain-clustering" or "brain-discovery-storage"));
        Assert.Contains(
            client.Resource.Annotations.OfType<WaitAnnotation>(),
            annotation => annotation.Resource.Name == "brain-discovery-storage");
    }

    private static IEnumerable<object> FlattenReferences(IEnumerable<object> values)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var value in values)
            foreach (var reference in FlattenReference(value, seen))
                yield return reference;
    }

    private static IEnumerable<object> FlattenReference(
        object value,
        HashSet<object> seen)
    {
        if (!seen.Add(value))
            yield break;

        yield return value;
        if (value is not IValueWithReferences references)
            yield break;

        foreach (var child in references.References)
            foreach (var descendant in FlattenReference(child, seen))
                yield return descendant;
    }

    private static Task<IExecutionConfigurationResult> ExecutionConfigurationAsync(IResource resource) =>
        ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(
                new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                NullLogger.Instance,
                CancellationToken.None);

    private static IResourceBuilder<DigitalBrainResource> CompleteBrain(
        IDistributedApplicationBuilder builder) =>
        builder.AddDigitalBrain("brain")
            .WithLLM<GptFast>().AsFast()
            .WithLLM<ClaudeBalanced>().AsBalanced()
            .WithLLM<GptReasoning>().AsReasoning()
            .WithEmbedding<TextEmbedding>();
}
