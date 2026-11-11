using DigitalBrain.Kernel;
using DigitalBrain.Units;

namespace DigitalBrain.Testing.Synapses;

/// <summary>What a <see cref="UnitScenario"/> run did and found.</summary>
/// <param name="Passed">True when the source deployed and every expectation held.</param>
/// <param name="Events">The signals journaled while each step ran, in order, with their trace ids.</param>
/// <param name="Failures">One human-readable line per failure, naming the step.</param>
/// <param name="Consented">The needs sentences of the installs the run approved on the person's behalf.</param>
/// <param name="Views">The view each state alias published by the end of the run, read through <see cref="IViewReader"/>.</param>
public sealed record ScenarioTranscript(
    bool Passed, IReadOnlyList<ScenarioEvent> Events, IReadOnlyList<string> Failures, IReadOnlyList<string> Consented, IReadOnlyList<PublishedView> Views);

/// <summary>A signal journaled while a step ran.</summary>
/// <param name="Step">The zero-based index of the step.</param>
/// <param name="Signal">The journaled signal; its sequence is the brain-wide position.</param>
public sealed record ScenarioEvent(int Step, JournaledSignal Signal);
