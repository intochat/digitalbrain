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

/// <summary>Expects some string value anywhere in the published view of <paramref name="Alias"/> to contain <paramref name="Contains"/> (ordinal).</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="Contains">The substring expected in at least one string value of the view.</param>
public sealed record ExpectView(string Alias, string Contains) : ScenarioStep;

/// <summary>Expects no signal with <paramref name="Alias"/> to have been journaled from the first step up to this one.</summary>
/// <param name="Alias">The signal alias.</param>
public sealed record ExpectNoSignal(string Alias) : ScenarioStep;

/// <summary>Expects a reaction to have failed since the stimulus step before it: a journaled <c>synapse.failed</c>, of <paramref name="Function"/> when given (the function name, with or without its type).</summary>
/// <param name="Function">The function name, with or without its type.</param>
public sealed record ExpectFailure(string? Function = null) : ScenarioStep;
