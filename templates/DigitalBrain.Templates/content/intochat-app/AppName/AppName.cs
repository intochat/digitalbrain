using DigitalBrain;
using DigitalBrain.Neurons;
using DigitalBrain.UI;
using DigitalBrain.Units;

namespace APP_NAMESPACE;

/// <summary>Asks the screen to show its start view.</summary>
[Signal("APP_ID.open")]
public sealed record Opened : Signal;

/// <summary>The turn state of <see cref="APP_CLASS"/>.</summary>
public sealed record APP_CLASSState(string Message)
{
    public APP_CLASSState() : this("Welcome to APP_CLASS.") { }
}

/// <summary>
/// The APP_CLASS screen. What it does is written in the spec comment next to this file; every operation described there is one
/// <c>[On]</c> method here.
/// </summary>
[Neuron("app.APP_ID")]
public sealed class APP_CLASS : Screen<APP_CLASSState>
{
    /// <summary>Shows the start view.</summary>
    [On]
    public Task Open(Opened signal) => Show(new APP_CLASSState("Welcome to APP_CLASS."));

    /// <summary>Renders the main view from <see cref="Screen{TState}.State"/>.</summary>
    protected override View Render(APP_CLASSState state) => new("APP_CLASS", new Column([new Text(state.Message)]));
}
