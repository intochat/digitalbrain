using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Core;

public static class DurablePayloadProtectionHosting
{
    private const string ConfigurationKey = "DigitalBrain:Security:StateProtectionKey";

    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var protector = new DurablePayloadProtector(
            configuration[ConfigurationKey]
            ?? throw new InvalidOperationException(
                $"Missing shared durable state-protection key '{ConfigurationKey}'."));
        services.TryAddSingleton<IDurablePayloadProtector>(protector);
    }
}
