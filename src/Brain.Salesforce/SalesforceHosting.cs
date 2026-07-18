using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Salesforce;

public static class SalesforceHosting
{
    public static ISiloBuilder AddBrainSalesforce(
        this ISiloBuilder silo,
        Func<IServiceProvider, ISalesforceMcpClient>? mcpFactory = null)
    {
        if (mcpFactory is null)
            silo.Services.AddSingleton<ISalesforceMcpClient, FakeSalesforceMcpClient>();
        else
            silo.Services.AddSingleton(mcpFactory);

        return silo;
    }
}
