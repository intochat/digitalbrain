using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire;

public static class TelegramAspireExtensions
{
    /// <summary>
    /// Wires the Telegram transport (<c>DigitalBrain.Telegram.Transport</c>) as an Aspire resource that bridges
    /// Telegram updates to the kernel gateway over gRPC. The transport boots no-op without a bot token, so the
    /// resource can be present from startup and configured later (token supplied at launch or via the in-app flow)
    /// with no AppHost restart.
    /// </summary>
    /// <param name="transport">The transport project, created in the AppHost via <c>AddProject&lt;Projects.DigitalBrain_Telegram_Transport&gt;(name)</c> so the generated <c>Projects.*</c> metadata type resolves.</param>
    /// <param name="kernel">The kernel/gateway resource whose gRPC endpoint the transport calls. Its grpc endpoint is injected as the gateway address.</param>
    /// <param name="botToken">Optional secret parameter carrying the Telegram bot token. When omitted (no token configured), the transport runs idle.</param>
    /// <param name="internalServiceKey">Shared service-to-service secret (same value injected into the kernel) that the transport presents on the secrets-returning <c>GetPackConfig</c> RPC. Server/transport-only — never exposed to the Flutter client config.</param>
    public static IResourceBuilder<ProjectResource> WireTelegramTransport(
        this DigitalBrainContext ctx,
        IResourceBuilder<ProjectResource> transport,
        IResourceBuilder<ProjectResource> kernel,
        IResourceBuilder<ParameterResource>? botToken = null,
        IResourceBuilder<ParameterResource>? internalServiceKey = null)
    {
        var kernelGrpc = kernel.GetEndpoint("grpc");

        transport = transport
            .WithReference(ctx.OrleansClient)
            .WithReference(kernel)
            .WaitFor(kernel)
            .WithEnvironment("DigitalBrain__GatewayAddress",
                ReferenceExpression.Create($"http://{kernelGrpc.Property(EndpointProperty.Host)}:{kernelGrpc.Property(EndpointProperty.Port)}"));

        if (botToken is not null)
        {
            transport = transport.WithEnvironment("Telegram__BotToken", botToken);
        }

        if (internalServiceKey is not null)
        {
            transport = transport.WithEnvironment("DigitalBrain__InternalServiceKey", internalServiceKey);
        }

        // Tell the transport which marketplace pack's stored config carries its bot token.
        // Matches the pack name in MarketplaceSeeds and the ConfigPack constant inside the pack code.
        transport = transport
            .WithEnvironment("Telegram__PackName", "DigitalBrain.Telegram.Responder")
            .WithEnvironment("Telegram__ConfigScope", "default");

        return transport;
    }
}
