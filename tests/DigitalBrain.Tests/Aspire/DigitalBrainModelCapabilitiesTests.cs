using DigitalBrain.Aspire;
using DigitalBrain.Core.Models;

namespace DigitalBrain.Tests.Aspire;

public class DigitalBrainModelCapabilitiesTests
{
    [Fact]
    public void ToolCapableModelDescribesItselfWithToolsCapabilityAndStableServiceKey()
    {
        var model = new ToolCapableTestModel();

        var descriptor = model.Describe();

        Assert.True(descriptor.Capabilities.SupportsTools);
        Assert.Equal("ollama-tool-capable-test", descriptor.ServiceKey);
    }

    [Fact]
    public void DefaultModelCapabilitiesIsFullyCapable()
    {
        var model = new DefaultTestModel();

        Assert.Equal(DigitalBrainModelCapabilities.FullyCapable, model.Describe().Capabilities);
    }

    [Fact]
    public void ServiceKeyNormalizesColonsAndDotsForUseAsADotnetKeyedServiceKey()
    {
        var descriptor = new DigitalBrainModelDescriptor(
            DigitalBrainCapabilityKind.LargeLanguageModel,
            "ollama",
            "qwen2.5-coder:1.5b",
            "Qwen 2.5 Coder 1.5B",
            DigitalBrainModelCapabilities.ChatOnly);

        Assert.Equal("ollama-qwen2-5-coder-1-5b", descriptor.ServiceKey);
    }

    private sealed class ToolCapableTestModel : LlmModel
    {
        public override string Provider => DigitalBrainProviderIds.Ollama;
        public override string Id => "tool-capable-test";
        public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
    }

    private sealed class DefaultTestModel : LlmModel
    {
        public override string Provider => DigitalBrainProviderIds.Ollama;
        public override string Id => "default-test";
    }
}
