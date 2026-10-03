using DigitalBrain.Flutter;
using DigitalBrain.Testing.E2E;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E;

public sealed class FlutterBackendFixture : SharedBrainFixture
{
    protected override Task<E2EBrain> StartHostAsync(CancellationToken cancellationToken)
        => E2ETest.Create().WithModule<FlutterModule, FlutterModuleOptions>(flutter => flutter.BackendOnly()).StartAsync(cancellationToken);
}

[CollectionDefinition(Name)]
public sealed class FlutterBackendCollection : ICollectionFixture<FlutterBackendFixture>
{
    public const string Name = "flutter-backend";
}
