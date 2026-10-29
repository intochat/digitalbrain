using DigitalBrain.Contracts;

namespace DigitalBrain;

/// <summary>Marks an executable BDD scenario method that verifies an <see cref="App"/> against its specification.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ScenarioAttribute : Attribute
{
    /// <summary>Marks an executable BDD scenario method with the <c>## Scenario:</c> title it verifies.</summary>
    /// <param name="title">The scenario title.</param>
    public ScenarioAttribute(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
    }

    /// <summary>The scenario title from the spec comment.</summary>
    public string Title { get; }
}

/// <summary>The outcome of running a single <see cref="ScenarioAttribute"/> scenario.</summary>
/// <param name="Title">The scenario title.</param>
/// <param name="Passed">True when the scenario completed without throwing or recording an app error.</param>
/// <param name="Error">The failure message when <paramref name="Passed"/> is false, or null.</param>
/// <param name="Elapsed">How long the scenario took.</param>
public sealed record ScenarioVerdict(string Title, bool Passed, string? Error, TimeSpan Elapsed);

/// <summary>The aggregate BDD verification report for an app, combining its operation baseline and all <see cref="ScenarioAttribute"/> scenarios.</summary>
/// <param name="AppId">The verified app id.</param>
/// <param name="Scenarios">The verdict for each scenario.</param>
/// <param name="BaselinePassed">True when every <c>[On]</c> operation passed the empty-input baseline.</param>
/// <param name="BaselineError">The baseline failure message when <paramref name="BaselinePassed"/> is false, or null.</param>
/// <param name="Elapsed">Total verification duration.</param>
public sealed record BddReport(
    string AppId,
    IReadOnlyList<ScenarioVerdict> Scenarios,
    bool BaselinePassed,
    string? BaselineError,
    TimeSpan Elapsed)
{
    /// <summary>True when both the operation baseline and every scenario passed.</summary>
    public bool Passed => BaselinePassed && Scenarios.All(static s => s.Passed);
}

/// <summary>Durable state of <see cref="BddNeuron"/>.</summary>
public sealed record BddState
{
    /// <summary>The latest verification report recorded for this app key, or null.</summary>
    public BddReport? LastReport { get; init; }
}

/// <summary>Published by <see cref="BddNeuron"/> when an app's BDD verification report is recorded.</summary>
/// <param name="AppId">The verified app id.</param>
/// <param name="Passed">True when the verification passed.</param>
/// <param name="Report">The full verification report.</param>
[Signal("bdd.verified")]
public sealed record BddVerified(string AppId, bool Passed, BddReport Report) : Signal;

/// <summary>Contract for the <c>bdd</c> verification neuron that stores and publishes <see cref="BddReport"/> results per app.</summary>
[NeuronAlias("bdd")]
public interface IBdd
{
    /// <summary>Records <paramref name="report"/> and publishes <see cref="BddVerified"/>.</summary>
    Task<BddReport> Record(BddReport report, CancellationToken cancellationToken = default);

    /// <summary>Returns the last recorded <see cref="BddReport"/>, or null if never verified.</summary>
    [ReadOnlyCall]
    Task<BddReport?> Read(CancellationToken cancellationToken = default);
}

/// <summary>Per-app BDD verification neuron (<c>bdd/{appId}</c>).</summary>
[Neuron("bdd")]
public sealed class BddNeuron : Neuron<BddState>, IBdd
{
    /// <inheritdoc />
    public async Task<BddReport> Record(BddReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        await Save(State with { LastReport = report }, new BddVerified(report.AppId, report.Passed, report)).ConfigureAwait(false);
        return report;
    }

    /// <inheritdoc />
    [ReadOnlyCall]
    public Task<BddReport?> Read(CancellationToken cancellationToken = default) => Task.FromResult(State.LastReport);
}
