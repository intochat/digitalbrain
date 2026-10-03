using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Kernel;

namespace DigitalBrain.Salesforce.Aspire.Hosting;

public sealed class SalesforceModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        var options = brain.GetModuleConfiguration<SalesforceModule>().GetModuleOptions<SalesforceModuleOptions>(nameof(SalesforceModule));
        if (options.HostMcp)
        {
            new DigitalBrainModuleBuilder<SalesforceModule>(brain).WithHostedMcp(options.PublicOrigin);
        }
    }
}
