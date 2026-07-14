namespace DigitalBrain.Tests.TestSupport;

[CollectionDefinition("kernel-host", DisableParallelization = true)]
public sealed class KernelHostCollection : ICollectionFixture<KernelWebApplicationFactory>;
