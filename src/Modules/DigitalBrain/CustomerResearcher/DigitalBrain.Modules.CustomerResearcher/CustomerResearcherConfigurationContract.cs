using DigitalBrain.Core;

namespace DigitalBrain.CustomerResearcher;

public sealed class CustomerResearcherModuleOptions
{
}

public sealed class CustomerResearcherConfigurationContract() : ModuleConfigurationContract<CustomerResearcherModule, CustomerResearcherModuleOptions>
{
    protected override ModuleDefinition Compile(CustomerResearcherModuleOptions options) => new(typeof(CustomerResearcherModule));
}
