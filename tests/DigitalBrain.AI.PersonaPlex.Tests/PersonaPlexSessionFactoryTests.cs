using DigitalBrain.AI.PersonaPlex;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.AI.PersonaPlex.Tests;

public sealed class PersonaPlexSessionFactoryTests
{
    [Fact]
    public async Task DisabledConfigurationReportsUnavailableWithoutOpeningOrtSessions()
    {
        await using var factory = new PersonaPlexSessionFactory(
            Options.Create(new PersonaPlexOptions
            {
                Enabled = false,
                ModelDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            }),
            NullLogger<PersonaPlexSessionFactory>.Instance);

        await factory.StartAsync(CancellationToken.None);

        Assert.Equal(PersonaPlexReadinessState.Disabled, factory.Readiness.State);
        Assert.False(factory.Readiness.IsModelConfigurationValid);
    }

    [Fact]
    public async Task DisabledConfigurationRejectsSessionCreation()
    {
        await using var factory = new PersonaPlexSessionFactory(
            Options.Create(new PersonaPlexOptions { Enabled = false }),
            NullLogger<PersonaPlexSessionFactory>.Instance);

        async Task CreateSessionAsync() =>
            await factory.CreateAsync(new PersonaPlexSessionRequest("connection-1"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(CreateSessionAsync);

        Assert.Equal("PersonaPlex is disabled.", exception.Message);
    }

    [Fact]
    public void ModelManifestRejectsTemporalGraphWithoutDeviceCacheOutputs()
    {
        var inputs = new HashSet<string>(["input_frame", "attention_mask"]);
        var outputs = new HashSet<string>(["hidden", "text_logits"]);
        for (var layer = 0; layer < 32; layer++)
        {
            inputs.Add($"past_key_values.{layer}.key");
            inputs.Add($"past_key_values.{layer}.value");
        }

        void Validate() => PersonaPlexModelManifest.ValidateTemporalNames(inputs, outputs);

        var exception = Assert.Throws<PersonaPlexModelManifestException>(Validate);

        Assert.Contains("present.31.value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnabledRuntimeFailureIsReportedWithoutExposingTheModelPath()
    {
        var modelDirectory = CreateInvalidFourGraphDirectory();
        try
        {
            await using var factory = new PersonaPlexSessionFactory(
                Options.Create(new PersonaPlexOptions
                {
                    Enabled = true,
                    ModelDirectory = modelDirectory,
                }),
                NullLogger<PersonaPlexSessionFactory>.Instance);

            await factory.StartAsync(CancellationToken.None);

            Assert.Equal(PersonaPlexReadinessState.Failed, factory.Readiness.State);
            Assert.True(factory.Readiness.IsModelConfigurationValid);
            Assert.Equal(
                "PersonaPlex CUDA runtime failed to load the configured model set.",
                factory.Readiness.Message);
            Assert.DoesNotContain(modelDirectory, factory.Readiness.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(modelDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task HostingRegistersOneFactoryForTheContractAndWarmupLifecycle()
    {
        var configuration = new ConfigurationManager
        {
            [$"{PersonaPlexOptions.SectionName}:Enabled"] = "false",
        };
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPersonaPlex(configuration);

        await using var provider = services.BuildServiceProvider();
        var concreteFactory = provider.GetRequiredService<PersonaPlexSessionFactory>();

        Assert.Same(concreteFactory, provider.GetRequiredService<IPersonaPlexSessionFactory>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => ReferenceEquals(service, concreteFactory));
    }

    private static string CreateInvalidFourGraphDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"personaplex-invalid-{Guid.NewGuid():N}");
        foreach (var graph in new[] { "mimi_encoder", "temporal", "depformer", "mimi_decoder" })
        {
            var graphDirectory = Directory.CreateDirectory(Path.Combine(directory, graph));
            File.WriteAllBytes(Path.Combine(graphDirectory.FullName, "model.onnx"), [0]);
        }

        return directory;
    }
}
