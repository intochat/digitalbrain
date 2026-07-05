using Aspire.Hosting.Testing;
using System.Reflection;

namespace DigitalBrain.Tests.Aspire;

// Real, executed coverage for the builder.ExecutionContext.IsRunMode branching added to
// AddDigitalBrain (storage emulator + Ollama container vs. AddConnectionString("qwen")
// placeholder). Uses DistributedApplicationTestingBuilder.CreateAsync against the real
// DigitalBrain.AppHost entry point (same reflection technique as DigitalBrainAppHostFixture)
// but deliberately stops after CreateAsync — never calling BuildAsync/StartAsync — so it only
// inspects the declared resource graph. No Docker, no Orleans, no container start: this runs in
// well under a second and needs no E2EPrerequisites opt-in, unlike the heavyweight render E2E
// suite that actually boots the app.
public sealed class AddDigitalBrainExecutionModeTests
{
    private static async Task<IDistributedApplicationTestingBuilder> CreateAppHostBuilderAsync(params string[] args)
    {
        var appHostAssembly = Assembly.Load("DigitalBrain.AppHost");
        var programType = appHostAssembly.GetTypes().FirstOrDefault(t => t.Name == "Program")
                          ?? appHostAssembly.EntryPoint?.DeclaringType
                          ?? throw new InvalidOperationException("Could not locate AppHost Program type for DistributedApplicationTestingBuilder.");

        return await DistributedApplicationTestingBuilder.CreateAsync(programType, args);
    }

    [Fact]
    public async Task RunMode_CreatesEmulatedStorageAndOllamaContainer()
    {
        // Default args == run mode, matching every `dotnet run`/`aspire run` invocation today.
        var builder = await CreateAppHostBuilderAsync();

        Assert.True(builder.ExecutionContext.IsRunMode);

        var storage = Assert.Single(builder.Resources, r => r.Name == "storage");
        Assert.Contains(storage.Annotations, a => a.GetType().Name == "EmulatorResourceAnnotation");

        Assert.Contains(builder.Resources, r => r.Name == "ollama" && r.GetType().Name == "OllamaResource");

        var qwen = Assert.Single(builder.Resources, r => r.Name == "qwen");
        Assert.Equal("OllamaModelResource", qwen.GetType().Name);

        // nomic-embed-text pulled into the same Ollama container as qwen (see Task 15).
        var embed = Assert.Single(builder.Resources, r => r.Name == "embed");
        Assert.Equal("OllamaModelResource", embed.GetType().Name);

        // Local Whisper (speaches) container for voice-to-text, always present in run mode (see Task 16).
        Assert.Contains(builder.Resources, r => r.Name == "whisper" && r.GetType().Name == "ContainerResource");
    }

    [Fact]
    public async Task PublishMode_SkipsEmulatorAndOllamaContainer_UsesConnectionStringPlaceholder()
    {
        // `aspire publish`'s equivalent: no containers should ever be started for this.
        var builder = await CreateAppHostBuilderAsync("--publisher", "manifest");

        Assert.True(builder.ExecutionContext.IsPublishMode);
        Assert.False(builder.ExecutionContext.IsRunMode);

        Assert.DoesNotContain(builder.Resources, r => r.Name == "ollama");

        var storage = Assert.Single(builder.Resources, r => r.Name == "storage");
        Assert.DoesNotContain(storage.Annotations, a => a.GetType().Name == "EmulatorResourceAnnotation");

        var qwen = Assert.Single(builder.Resources, r => r.Name == "qwen");
        Assert.Equal("ConnectionStringParameterResource", qwen.GetType().Name);

        // No local Ollama container in publish mode, so no "embed" model resource either (see Task 15).
        Assert.DoesNotContain(builder.Resources, r => r.Name == "embed");

        // No local Whisper container in publish mode either (see Task 16).
        Assert.DoesNotContain(builder.Resources, r => r.Name == "whisper");
    }
}
