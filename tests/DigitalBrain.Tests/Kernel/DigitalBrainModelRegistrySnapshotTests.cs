using DigitalBrain.Core.Models;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class DigitalBrainModelRegistrySnapshotTests
{
    [Fact]
    public void ReadsFullRegistrationsIncludingServiceKeyAndCapabilities()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "ollama",
            ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "qwen2.5-coder:1.5b",
            ["DigitalBrain:ModelRegistry:Registrations:0:DisplayName"] = "Qwen 2.5 Coder 1.5B",
            ["DigitalBrain:ModelRegistry:Registrations:0:Role"] = "Balanced",
            ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "ollama-qwen2-5-coder-1-5b",
            ["DigitalBrain:ModelRegistry:Registrations:0:SupportsTools"] = "false",
            ["DigitalBrain:ModelRegistry:Registrations:1:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:1:Provider"] = "ollama",
            ["DigitalBrain:ModelRegistry:Registrations:1:Id"] = "llama3.1:8b",
            ["DigitalBrain:ModelRegistry:Registrations:1:DisplayName"] = "Llama 3.1 8B",
            ["DigitalBrain:ModelRegistry:Registrations:1:Role"] = "Reasoning",
            ["DigitalBrain:ModelRegistry:Registrations:1:ServiceKey"] = "ollama-llama3-1-8b",
            ["DigitalBrain:ModelRegistry:Registrations:1:SupportsTools"] = "true",
        });

        var entries = DigitalBrainModelRegistrySnapshot.Read(config);

        Assert.Equal(2, entries.Count);
        var toolCapable = DigitalBrainModelRegistrySnapshot.FirstOrDefault(
            entries, DigitalBrainCapabilityKind.LargeLanguageModel, e => e.Capabilities.SupportsTools);
        Assert.NotNull(toolCapable);
        Assert.Equal("ollama-llama3-1-8b", toolCapable!.ServiceKey);
    }

    [Fact]
    public void FirstOrDefaultReturnsNullWhenNoRegistrationMatches()
    {
        var entries = DigitalBrainModelRegistrySnapshot.Read(BuildConfig(new Dictionary<string, string?>()));

        var result = DigitalBrainModelRegistrySnapshot.FirstOrDefault(entries, DigitalBrainCapabilityKind.VoiceToText);

        Assert.Null(result);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
