using System.Runtime.Loader;
using DigitalBrain.Apps.Hosting;
using DigitalBrain.UI;

namespace DigitalBrain.Runtime.Apps;

internal sealed class HostedProgram(
    string owner,
    string hash,
    AppTurns turns,
    IReadOnlyDictionary<string, string> settings,
    AssemblyLoadContext? context,
    BehaviorView[] behaviors) : IPresenter
{
    private View? _presentedSide;
    private View? _presentedMain;

    public string Owner { get; } = owner;

    public string Hash { get; } = hash;

    public AppTurns Turns { get; } = turns;

    public IReadOnlyDictionary<string, string> Settings { get; } = settings;

    public AssemblyLoadContext? Context { get; private set; } = context;

    public BehaviorView[] Behaviors { get; } = behaviors;

    public void Present(View? side, View? main)
    {
        _presentedSide = side ?? _presentedSide;
        _presentedMain = main ?? _presentedMain;
    }

    public void Toast(Toast toast)
    {
    }

    public (View? Side, View? Main) ConsumePresented() => (_presentedSide, _presentedMain);

    public void Unload()
    {
        var ctx = Context;
        Context = null;
        ctx?.Unload();
    }
}
