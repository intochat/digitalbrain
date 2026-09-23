namespace DigitalBrain.Broker.Sandbox;

// Builds the isolation contract for one process app call. The spec is deliberately not a free
// parameter: network is always denied and the Orleans gateway is always absent, whatever the
// publisher declares. Only the limits and the image come from configuration and the package.
internal static class SandboxSpecFactory
{
    public static SandboxSpec Create(ProcessAppPackage package, SandboxOptions options, string operation)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);
        return new SandboxSpec
        {
            AppId = package.Manifest.Id,
            Image = package.Sandbox.Image,
            Command = package.Sandbox.Command,
            Limits = new SandboxLimits
            {
                CpuCount = options.CpuCount,
                MemoryBytes = options.MemoryBytes,
                ProcessCount = options.ProcessCount,
                Timeout = options.Timeout,
            },
            Network = SandboxNetworkPolicy.Denied,
            OrleansGateway = false,
            ReadOnlyRootFilesystem = true,
            Environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["INTOCHAT_APP_ID"] = package.Manifest.Id,
                ["INTOCHAT_PUBLISHER"] = package.Manifest.Publisher,
                ["INTOCHAT_OPERATION"] = operation,
            },
        };
    }
}