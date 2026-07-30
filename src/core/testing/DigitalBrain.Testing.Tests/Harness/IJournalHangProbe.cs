using DigitalBrain.Abstractions;

namespace DigitalBrain.TestingTests.Harness;

[ClientEntryPoint]
public partial interface IJournalHangProbe : INeuron
{
    [Alias(nameof(EmitWhileObserverReenters))]
    Task EmitWhileObserverReenters(string greeterName, string watcherName, string guest);

    [Alias(nameof(EmitWhileObserverIsStuck))]
    Task EmitWhileObserverIsStuck(string greeterName, string guest, IJournalObserver stuckObserver);

    [Alias(nameof(Reentries))]
    Task<int> Reentries(string watcherName);
}
