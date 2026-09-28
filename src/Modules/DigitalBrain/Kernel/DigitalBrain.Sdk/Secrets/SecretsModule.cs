using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Sdk.Secrets;

public sealed class SecretsModule : IModule
{
    public void Configure(ISiloBuilder silo)
    {
        silo.Services.TryAddSingleton<IKeyVault, FakeKeyVault>();
        if (OperatingSystem.IsWindows())
        {
            silo.Services.TryAddSingleton<IKeyWrapper, DpapiKeyWrapper>();
        }
        else
        {
            silo.Services.TryAddSingleton<IKeyWrapper, KeyVaultKeyWrapper>();
        }
    }

    public void Configure(IEndpointRouteBuilder endpoints) => endpoints.MapSecrets();
}
