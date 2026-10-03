using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Google.Gmail;

public sealed class GmailModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        var options = brain.GetModuleConfiguration<GmailModule>().GetModuleOptions<GmailModuleOptions>(nameof(GmailModule));
        if (options.HostGmail) { new DigitalBrainModuleBuilder<GmailModule>(brain).WithGmail(); }
    }
}
