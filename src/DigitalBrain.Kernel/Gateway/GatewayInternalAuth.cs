using Grpc.Core;

namespace DigitalBrain.Kernel.Gateway;

// gRPC metadata header carrying the shared service-to-service secret. Lower-case per gRPC ASCII-header rules.
public static class GatewayInternalAuth
{
    internal const string InternalKeyHeader = "x-internal-key";

    // Reject any caller that cannot prove it is an internal transport. The kernel is configured with a shared
    // InternalServiceKey (injected as an env param to both the kernel and the internal transport); the transport
    // presents it as the x-internal-key metadata header. Constant-time compare avoids leaking the key by timing.
    // When NO key is configured: allow only in Development (local "clone + run" convenience), deny otherwise — so a
    // misconfigured production kernel fails closed rather than exposing secrets to the open ingress.
    public static void Enforce(IConfiguration configuration, IHostEnvironment environment, ILogger logger, ServerCallContext context, string callerName)
    {
        var configuredKey = configuration["DigitalBrain:InternalServiceKey"];

        if (string.IsNullOrEmpty(configuredKey))
        {
            if (environment.IsDevelopment())
                return;
            logger.LogError("{Caller} denied: no InternalServiceKey configured outside Development.", callerName);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "internal only"));
        }

        var presented = context.RequestHeaders.GetValue(InternalKeyHeader);
        if (presented is null || !FixedTimeEquals(presented, configuredKey))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "internal only"));
    }

    private static bool FixedTimeEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
}
