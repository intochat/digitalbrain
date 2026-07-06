using DigitalBrain.Tests.TestSupport;

namespace DigitalBrain.Tests.Gateway;

/// <summary>
/// Collection fixture providing a single shared kernel WebApplicationFactory configured for test mode.
/// Disables parallelization because the co-hosted Orleans silo + gRPC listeners are not safe for concurrent test hosts.
/// </summary>
[CollectionDefinition("kernel-host", DisableParallelization = true)]
public sealed class KernelHostCollection : ICollectionFixture<KernelWebApplicationFactory>
{
}
