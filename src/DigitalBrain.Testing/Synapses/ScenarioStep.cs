using System.Text.Json;

namespace DigitalBrain.Testing.Synapses;

/// <summary>One step of a <see cref="UnitScenario"/>: a stimulus or an expectation.</summary>
public abstract record ScenarioStep;

/// <summary>Journals a signal by its alias into the brain, so alias reactions fire.</summary>
/// <param name="SignalAlias">The signal's alias.</param>
/// <param name="Payload">The signal's payload.</param>
public sealed record Publish(string SignalAlias, JsonElement Payload) : ScenarioStep;

/// <summary>Advances the scenario clock to <paramref name="At"/>, firing every timer due by then.</summary>
/// <param name="At">The instant to advance to; the clock starts at 2026-01-01 UTC.</param>
public sealed record Tick(DateTimeOffset At) : ScenarioStep;

/// <summary>Expects the state of <paramref name="Alias"/> to hold <paramref name="Expected"/> at a dotted path.</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="JsonPath">A dotted path of property names, matched case-insensitively (<c>Unseen</c>, <c>Draft.Title</c>).</param>
/// <param name="Expected">The expected JSON value.</param>
public sealed record ExpectState(string Alias, string JsonPath, JsonElement Expected) : ScenarioStep;

/// <summary>Expects a signal with <paramref name="Alias"/> to have been journaled by the stimulus step before it.</summary>
/// <param name="Alias">The signal alias.</param>
public sealed record ExpectSignal(string Alias) : ScenarioStep;
