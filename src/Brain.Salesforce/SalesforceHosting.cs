using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Salesforce;

public static class SalesforceHosting
{
    public static ISiloBuilder AddBrainSalesforce(
        this ISiloBuilder silo,
        Func<IServiceProvider, ISalesforceMcpClient> mcpFactory)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(mcpFactory);
        silo.Services.AddSingleton(mcpFactory);
        return silo;
    }
}
