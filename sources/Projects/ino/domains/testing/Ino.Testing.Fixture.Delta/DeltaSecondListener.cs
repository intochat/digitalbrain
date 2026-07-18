using Ino.Core.Hosting;
using Ino.Testing.Fixtures.DeltaContracts;
using Orleans;

namespace Ino.Testing.Fixture;

public sealed class DeltaSecondListener(IInoTestCapture? capture = null)
    : Grain, IReactsTo<SomethingObserved>
{
    public Task ReactAsync(SomethingObserved synapse, NeuronContext ctx, CancellationToken ct)
    {
        capture?.Record(typeof(DeltaSecondListener), synapse);
        return Task.CompletedTask;
    }
}
