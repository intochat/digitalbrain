using DigitalBrain.UI;

namespace DigitalBrain.Testing.Views;

/// <summary>The JSON fixture recorded for a single view of a screen.</summary>
public sealed record ViewFixture(string AppId, string Scenario, View? Side, View? Main);

/// <summary>Thrown when a recorded view fixture on disk does not match the view produced.</summary>
public sealed class ViewFixtureMismatchException(string message) : Exception(message);
