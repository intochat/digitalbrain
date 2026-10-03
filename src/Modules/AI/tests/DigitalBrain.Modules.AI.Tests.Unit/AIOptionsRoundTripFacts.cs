using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Modules.AI.Tests.Unit;

public sealed class AIOptionsRoundTripFacts
{
    [Fact]
    public void PopulatedDictionariesSurviveCompileAndBindByValue()
    {
        var options = new AIOptions();
        options.ModelProfiles["Fast"] = new AIModelProfileOptions { Provider = "OpenAI", Model = "m1", ContextWindowTokens = 1000 };
        options.Ollama.Models["Gemma"] = new AIModelOptions { Model = "gemma:2b" };
        var definition = ModuleOptionsSerialization.Compile<AIModule, AIOptions>(options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(definition.Configuration).Build();

        var bound = configuration.GetModuleOptions<AIOptions>("ai");

        Assert.Equal("m1", bound.ModelProfiles["fast"].Model);
        Assert.Equal(1000, bound.ModelProfiles["Fast"].ContextWindowTokens);
        Assert.Equal("gemma:2b", bound.Ollama.Models["gemma"].Model);
    }
}
