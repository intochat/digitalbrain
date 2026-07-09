using System.Reflection;
using Aspire.Hosting.Testing;

namespace DigitalBrain.Tests.Aspire;

// Real, executed coverage for the builder.ExecutionContext.IsRunMode branching added to
// AddDigitalBrain (storage emulator + Ollama container vs. AddConnectionString("qwen")
// placeholder). Uses DistributedApplicationTestingBuilder.CreateAsync against the real
// DigitalBrain.AppHost entry point.
// but deliberately stops after CreateAsync — never calling BuildAsync/StartAsync — so it only
// inspects the declared resource graph. No Docker, no Orleans, no container start.
public sealed class AddDigitalBrainExecutionModeTests
{
    private static async Task<IDistributedApplicationTestingBuilder> CreateAppHostBuilderAsync(params string[] args)
    {
        Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, "DigitalBrain.AppHost.dll")),
            "DigitalBrain.AppHost must be built and copied for AppHost model tests.");

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

        // llama3.1:8b (Llama31_8B, registered .AsReasoning() for Ino's tool-calling path in AppHost.cs) must
        // also be pre-pulled into the same Ollama container — otherwise Ino's tool-capable model resolution
        // points at a tag Ollama never actually has, and the first real call to it fails with "model not found".
        var reasoningModel = Assert.Single(builder.Resources, r => r.Name == "llama3-1-8b");
        Assert.Equal("OllamaModelResource", reasoningModel.GetType().Name);

        // Local Whisper (speaches) container for voice-to-text, always present in run mode (see Task 16).
        Assert.Contains(builder.Resources, r => r.Name == "whisper" && r.GetType().Name == "ContainerResource");

        // Sync blob container (checkpoint backup/restore, M11 Task 20) — unconditional like grainstate/journal,
        // not gated by isRunMode, but still present here as a regression guard for the resource wiring itself.
        Assert.Contains(builder.Resources, r => r.Name == "sync" && r.GetType().Name == "AzureBlobStorageResource");
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

        // Nor the reasoning-tier llama3.1:8b pre-pull — it only exists inside the run-mode-only Ollama container.
        Assert.DoesNotContain(builder.Resources, r => r.Name == "llama3-1-8b");

        // No local Whisper container in publish mode either (see Task 16).
        Assert.DoesNotContain(builder.Resources, r => r.Name == "whisper");

        // Sync blob container still present in publish mode (AddAzureStorage produces a valid real-Azure
        // resource on its own — same reasoning as grainstate/journal, see Task 20).
        Assert.Contains(builder.Resources, r => r.Name == "sync" && r.GetType().Name == "AzureBlobStorageResource");
    }
}
