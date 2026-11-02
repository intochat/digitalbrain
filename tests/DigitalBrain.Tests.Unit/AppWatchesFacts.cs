namespace DigitalBrain.Tests.Unit;

public sealed class AppWatchesFacts
{
    [Signal("tests.watch.clicked")]
    public sealed record Clicked(string Action) : Signal;

    [Signal("tests.watch.changed")]
    public sealed record Changed(int Revision) : Signal;

    public interface IView;

    public interface IRegistry
    {
        string[] Members { get; }
    }

    public interface IButton;

    private sealed class Registry(string[] members) : IRegistry
    {
        public string[] Members { get; } = members;
    }

    [App("board")]
    private sealed class Board : App
    {
        public List<string> Heard { get; } = [];

        protected override void Watches(WatchBuilder watch)
        {
            watch.On<IView, Clicked>("view", at => at.App + "/view", clicked => { Heard.Add("view:" + clicked.Action); return Task.CompletedTask; });
            watch.OnEach<IRegistry, Changed, IButton, Clicked>("buttons", at => at.Brain + "/registry",
                (registry, at, _) => Task.FromResult(registry.Members.Select(member => at.App + "/" + member).ToArray()), clicked => { Heard.Add("button:" + clicked.Action); return Task.CompletedTask; });
        }
    }

    [App("twice")]
    private sealed class Twice : App
    {
        protected override void Watches(WatchBuilder watch)
        {
            watch.On<IView, Clicked>("same", at => at.App, _ => Task.CompletedTask);
            watch.On<IView, Clicked>("same", at => at.App, _ => Task.CompletedTask);
        }
    }

    [App("quiet")]
    private sealed class Quiet : App;

    private static readonly AppAddress At = new("ada", "ada/apps/board");

    [Fact]
    public async Task A_fixed_watch_is_keyed_from_the_app_and_runs_its_handler()
    {
        var board = new Board();
        var view = board.DeclaredWatches().Single(watch => watch.Name == "view");

        Assert.False(view.IsSet);
        Assert.Equal(typeof(IView), view.SourceType);
        Assert.Equal(typeof(Clicked), view.SignalType);
        Assert.Equal("ada/apps/board/view", view.Key(At));
        await view.Handle(new Clicked("tab:general"));
        Assert.Equal(["view:tab:general"], board.Heard);
    }

    [Fact]
    public async Task A_set_watch_selects_its_members_from_the_registry()
    {
        var buttons = new Board().DeclaredWatches().Single(watch => watch.Name == "buttons");

        Assert.True(buttons.IsSet);
        Assert.Equal(typeof(Changed), buttons.ChangeType);
        Assert.Equal(typeof(IButton), buttons.MemberType);
        Assert.Equal("ada/registry", buttons.Key(At));
        Assert.Equal(["ada/apps/board/a", "ada/apps/board/b"], await buttons.Select!(new Registry(["a", "b"]), At, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void An_app_without_watches_declares_none_and_names_are_unique()
    {
        Assert.Empty(new Quiet().DeclaredWatches());
        Assert.Throws<ArgumentException>(() => new Twice().DeclaredWatches());
    }
}
