using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Salesforce.Aspire.Hosting;

public sealed class SalesforceModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        var configuration = brain.GetModuleConfiguration<SalesforceModule>();
        if (configuration.GetValue<bool>("DigitalBrain:Salesforce:Hosting:HostMcp"))
        {
            new DigitalBrainModuleBuilder<SalesforceModule>(brain).WithHostedMcp(
                new Uri(configuration[SalesforceModule.McpEndpointConfigurationKey]!),
                configuration["DigitalBrain:Salesforce:Hosting:PublicOrigin"] is { Length: > 0 } origin ? new Uri(origin) : null);
        }
    }
}
