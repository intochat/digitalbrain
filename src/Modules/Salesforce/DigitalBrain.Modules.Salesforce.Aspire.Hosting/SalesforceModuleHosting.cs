using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Salesforce.Aspire.Hosting;

public sealed class SalesforceModuleHosting : IDigitalBrainModuleHosting
{
    public string Id => "salesforce";
    public void Configure(DigitalBrainBuilder brain)
    {
        var options = brain.GetModuleConfiguration("salesforce").GetModuleOptions<SalesforceModuleOptions>("salesforce");
        if (options.HostMcp)
        {
            new DigitalBrainModuleBuilder<DigitalBrain.Salesforce.Aspire.Hosting.SalesforceModuleHosting>(brain).WithHostedMcp(options.PublicOrigin);
        }
    }
}
