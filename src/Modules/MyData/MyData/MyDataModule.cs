using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.MyData;

public sealed class MyDataModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(MyDataModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.Services.TryAddSingleton<IKeyVault, FakeKeyVault>();
        if (OperatingSystem.IsWindows())
        {
            silo.Services.TryAddSingleton<IKeyWrapper, DpapiKeyWrapper>();
        }
        else
        {
            silo.Services.TryAddSingleton<IKeyWrapper, KeyVaultKeyWrapper>();
        }
        silo.Services.TryAddSingleton<ISecretResolver, SecretResolver>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapMyData();
    }
}
