using Ino.Core.Hosting;
using Ino.Testing.Fixtures.DeltaContracts;
using Orleans;

namespace Ino.Testing.Fixture;

public sealed class DeltaFirstListener(IInoTestCapture? capture = null)
    : Grain, IReactsTo<SomethingObserved>
{
    public Task ReactAsync(SomethingObserved synapse, NeuronContext ctx, CancellationToken ct)
    {
        capture?.Record(typeof(DeltaFirstListener), synapse);
        return Task.CompletedTask;
    }
}
