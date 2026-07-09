using DigitalBrain.Core.Models;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class DigitalBrainChatClientRegistrationTests
{
    [Fact]
    public void RegistersOneKeyedChatClientPerLlmRegistration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "ollama",
            ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "qwen2.5-coder:1.5b",
            ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "ollama-qwen2-5-coder-1-5b",
            ["DigitalBrain:ModelRegistry:Registrations:0:Role"] = "Fast",
            ["DigitalBrain:ModelRegistry:Registrations:1:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:1:Provider"] = "ollama",
            ["DigitalBrain:ModelRegistry:Registrations:1:Id"] = "llama3.1:8b",
            ["DigitalBrain:ModelRegistry:Registrations:1:ServiceKey"] = "ollama-llama3-1-8b",
            ["DigitalBrain:ModelRegistry:Registrations:1:Role"] = "Reasoning",
            ["DigitalBrain:ModelRegistry:Registrations:1:SupportsTools"] = "true",
            ["DigitalBrain:Llm:OllamaEndpoint"] = "http://localhost:11434",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDigitalBrainChatClients(config);
        var provider = services.BuildServiceProvider();

        var fast = provider.GetKeyedService<IChatClient>("ollama-qwen2-5-coder-1-5b");
        var reasoning = provider.GetKeyedService<IChatClient>("ollama-llama3-1-8b");

        Assert.NotNull(fast);
        Assert.NotNull(reasoning);
        Assert.NotSame(fast, reasoning);
    }
}
