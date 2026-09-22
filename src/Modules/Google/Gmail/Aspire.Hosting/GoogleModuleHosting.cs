using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Google.Gmail;

public sealed class GmailModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        var options = brain.GetModuleConfiguration<GmailModule>().GetSection(GmailModule.GmailOAuthConfigurationRoot).Get<GmailModuleOptions>() ?? new();
        if (options.HostGmail) { new DigitalBrainModuleBuilder<GmailModule>(brain).WithGmail(options.PublicOrigin); }
    }
}