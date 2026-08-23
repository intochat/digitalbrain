using DigitalBrain.Abstractions;
using DigitalBrain.Execution;
using DigitalBrain.Integrations.Gmail;
using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Integrations.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Integrations;

public sealed class IntegrationsModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<ICapabilityHandler, GmailSearchHandler>();
        builder.Services.AddSingleton<ICapabilityHandler, SalesforceUpsertHandler>();
        builder.Services.AddSingleton<ICapabilityHandler, WebSearchHandler>();

        if (UseFakeTransports(builder.Configuration))
        {
            builder.Services.TryAddSingleton<IGmailTransport, FakeGmailTransport>();
            builder.Services.TryAddSingleton<ISalesforceTransport, FakeSalesforceTransport>();
            builder.Services.TryAddSingleton<IWebSearchTransport, FakeWebSearchTransport>();
        }
        else
        {
            builder.Services.TryAddSingleton<IGmailTransport, NotImplementedGmailTransport>();
            builder.Services.TryAddSingleton<ISalesforceTransport, NotImplementedSalesforceTransport>();
            builder.Services.TryAddSingleton<IWebSearchTransport, NotImplementedWebSearchTransport>();
        }
    }

    private static bool UseFakeTransports(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        if (string.Equals(
                configuration[DigitalBrainNames.Mode],
                DigitalBrainNames.TestingMode,
                StringComparison.Ordinal))
        {
            return true;
        }

        var fakes = configuration[DigitalBrainNames.Fakes];
        return string.Equals(fakes, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fakes, "1", StringComparison.OrdinalIgnoreCase);
    }
}
