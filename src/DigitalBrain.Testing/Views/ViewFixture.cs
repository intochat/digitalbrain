using DigitalBrain.UI;

namespace DigitalBrain.Testing.Views;

/// <summary>The JSON fixture recorded for a single BDD scenario of an app.</summary>
public sealed record ViewFixture(string AppId, string Scenario, View? Side, View? Main);

/// <summary>Thrown when a scenario's recorded view fixture on disk does not match the view produced by the scenario.</summary>
public sealed class ViewFixtureMismatchException(string message) : Exception(message);
