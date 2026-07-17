using Brain.Kernel;
using Brain.Modules.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace Brain.Modules.Salesforce;

public static class SalesforceHosting
{
    public static ISiloBuilder AddBrainSalesforce(this ISiloBuilder silo, IConfiguration config)
    {
        silo.AddBrainKind("salesforce", sp => new SalesforceKind(sp.GetRequiredService<IGrainFactory>(), sp));

        var options = SalesforceProviderOptions.FromConfiguration(config);
        if (options is null)
        {
            silo.Services.AddKeyedSingleton<ISalesforceProvider>("salesforce", new DevSalesforceProvider());
            return silo;
        }

        silo.Services.AddHttpClient();
        silo.Services.AddKeyedSingleton<IConnectionProvider>("salesforce", (sp, _) => new SalesforceHttpProvider(sp.GetRequiredService<IHttpClientFactory>(), options));
        silo.Services.AddKeyedSingleton<ISalesforceProvider>("salesforce", (sp, _) => new SalesforceHttpProvider(sp.GetRequiredService<IHttpClientFactory>(), options));
        return silo;
    }
}
