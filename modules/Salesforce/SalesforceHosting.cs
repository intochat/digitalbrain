using Orleans.Hosting;

namespace Salesforce;

public static class SalesforceHosting
{
    public static ISiloBuilder AddSalesforce(this ISiloBuilder silo)
    {
        _ = typeof(SalesforceNeuron);
        return silo;
    }
}
