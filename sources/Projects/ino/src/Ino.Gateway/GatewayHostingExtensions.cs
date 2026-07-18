using Microsoft.Extensions.DependencyInjection;

namespace Ino.Gateway;

public static class GatewayHostingExtensions
{
    /// <summary>
    /// Register <see cref="IInoGateway"/> + default implementation as a
    /// singleton. Transport projects (Ino.Gateway.Grpc / .Mcp / .Cli) layer
    /// their own hosting on top and resolve <see cref="IInoGateway"/> from DI.
    /// </summary>
    public static IServiceCollection AddInoGateway(this IServiceCollection services)
    {
        services.AddSingleton<IInoGateway, InoGateway>();
        return services;
    }
}
