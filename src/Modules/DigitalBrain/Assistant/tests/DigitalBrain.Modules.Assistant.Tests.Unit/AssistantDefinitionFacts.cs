using DigitalBrain.Assistant;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class AssistantDefinitionFacts
{
    [Fact]
    public void DefaultDefinitionStartsWithDiscoveryInsteadOfSelectingInstalledTools()
    {
        var definition = AssistantDefinition.For();
        Assert.Equal(["discover_capabilities", "table_read", "table_refine", "show_form", "show_view"], definition.Tools);
    }
    [Fact]
    public void TheHostCanSupplyTheAssistantNameAndPackageGuidance()
    {
        var definition = AssistantDefinition.For(options: new AssistantOptions
        {
            DisplayName = "Example assistant",
            Instructions = "Look for results in the installed research package.",
        });
        Assert.Equal("Example assistant", definition.DisplayName);
        Assert.Contains("Look for results in the installed research package.", definition.Instructions);
    }

    [Fact]
    public void DefaultInstructionsDoNotAssumeAProductOrAnInstalledPackage()
    {
        Assert.DoesNotContain("IntoChat", AssistantDefinition.Product.Instructions);
        Assert.DoesNotContain("customer_research", AssistantDefinition.Product.Instructions);
    }
}
