using DigitalBrain.Runtime.Orleans.Azure.Tests.Integration;
using DigitalBrain.Testing.Conformance.Host;

namespace DigitalBrain.Framework.Tests.E2E;

// The packaged host-conformance facts on the framework's own targets: TestOS, the local topology and the cloud topology on Azurite.
public sealed class FrameworkHostConformanceFacts(AzuriteFixture azurite) : HostConformanceFacts
{
    public static TheoryData<string> TargetNames => new(TestOSTarget.TargetName, LocalOrleansTarget.TargetName, AzuriteOrleansTarget.TargetName);

    protected override IEnumerable<FrameworkTarget> Targets => [new TestOSTarget(), new LocalOrleansTarget(), new AzuriteOrleansTarget(azurite)];
}
