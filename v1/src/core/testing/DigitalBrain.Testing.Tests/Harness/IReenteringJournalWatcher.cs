using DigitalBrain.Abstractions;

namespace DigitalBrain.TestingTests.Harness;

public partial interface IReenteringJournalWatcher : INeuron, IJournalObserver
{
    [Alias(nameof(Arm))]
    Task Arm(string subjectName);

    [Alias(nameof(Reentries))]
    Task<int> Reentries();
}
