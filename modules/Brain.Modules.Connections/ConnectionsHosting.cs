using Brain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace Brain.Modules.Connections;

public static class ConnectionsHosting
{
    public static ISiloBuilder AddBrainConnections(this ISiloBuilder silo, IConfiguration config)
    {
        silo.AddBrainKind("connection", sp => new ConnectionKind(sp));
        silo.Services.AddKeyedSingleton<IConnectionProvider>("google", new DevConnectionProvider());
        silo.Services.AddKeyedSingleton<IConnectionProvider>("salesforce", new DevConnectionProvider());
        return silo;
    }
}
