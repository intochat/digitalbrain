using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Google.Aspire.Hosting;

public sealed class GoogleModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        var options = brain.GetModuleConfiguration<GoogleModule>().GetSection(GoogleModule.GmailOAuthConfigurationRoot).Get<GoogleModuleOptions>() ?? new();
        if (options.HostGmail) { new DigitalBrainModuleBuilder<GoogleModule>(brain).WithGmail(options.PublicOrigin); }
    }
}
