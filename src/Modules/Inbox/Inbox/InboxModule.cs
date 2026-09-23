using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Inbox;

[ModuleConfiguration(typeof(InboxConfigurationContract))]
public sealed class InboxModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(InboxModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton<IEmailSender, InMemoryEmailSender>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapInbox();
    }
}
