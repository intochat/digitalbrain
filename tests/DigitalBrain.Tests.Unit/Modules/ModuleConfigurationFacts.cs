using DigitalBrain.Modules.Configuration;
using DigitalBrain.Modules.Options;

namespace DigitalBrain.Tests.Unit.Modules;

public sealed class ModuleConfigurationFacts
{
    [Fact]
    public void ModuleSectionUsesTheConfiguredRoot()
    {
        var previous = ModuleConfigurationOptions.Current;
        try
        {
            ModuleConfigurationOptions.Current = new();
            Assert.Equal("DigitalBrain:Modules:gmail", ModuleConfiguration.ModuleSection(new ModuleId("gmail")));
            Assert.Equal("DigitalBrain:Modules:gmail", ModuleConfiguration.ModuleSection("gmail"));
            Assert.Equal("DigitalBrain:Modules:gmail:Options", ModuleConfiguration.ModuleOptionsSection("gmail"));

            ModuleConfigurationOptions.Current = new() { Root = "IntoChatOS" };
            Assert.Equal("IntoChatOS:Modules:gmail", ModuleConfiguration.ModuleSection(new ModuleId("gmail")));
            Assert.Equal("IntoChatOS:Modules:gmail", ModuleConfiguration.ModuleSection("gmail"));
            Assert.Equal("IntoChatOS:Modules:gmail:Options", ModuleConfiguration.ModuleOptionsSection("gmail"));
        }
        finally
        {
            ModuleConfigurationOptions.Current = previous;
        }
    }
}
