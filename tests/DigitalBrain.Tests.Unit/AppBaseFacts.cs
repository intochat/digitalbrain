namespace DigitalBrain.Tests.Unit;

public sealed class AppBaseFacts
{
    [App("notes")]
    private sealed class Notes : App
    {
        public AppKey Root(BrainId brain) => Key(brain);

        public RequestLedger Ledger => Requests;

        public bool Show(Action<string?, string?> publish, Func<string?> side, Func<string?> main) => Present(publish, side, main);

        public void Fail(Exception error) => RecordError(error);

        public void Recover() => ClearError();

        public void Lose(Exception error, string reason) => Swallow(error, reason);
    }

    private sealed class Unmarked : App;

    private static readonly BrainId Ada = new("ada");

    [Fact]
    public void AppId_comes_from_the_App_attribute()
    {
        Assert.Equal("notes", new Notes().AppId);
        Assert.Throws<InvalidOperationException>(() => new Unmarked().AppId);
    }

    [Fact]
    public void Key_parts_hang_from_the_app_key_and_reject_slashes()
    {
        var key = new Notes().Root(Ada) / "slot" / 3;
        Assert.Equal("notes/slot/3", key.Path);
        Assert.Equal("notes/slot/3", (string)key);
        Assert.Equal(["notes", "slot", "3"], key.Parts);
        Assert.Equal("notes", key.App);
        Assert.Equal(Ada, key.Brain);
        Assert.Equal(new NeuronId(Ada, "text", "notes/slot/3"), key.Neuron("text"));
        Assert.Throws<ArgumentException>(() => key / "a/b");
        Assert.Throws<ArgumentException>(() => key / string.Empty);
        Assert.Equal(key, AppKey.Parse(Ada, "notes/slot/3"));
        Assert.Throws<ArgumentException>(() => AppKey.Parse("notes//x"));
        Assert.Throws<InvalidOperationException>(() => AppKey.Parse("notes").Neuron("text"));
        Assert.Throws<InvalidOperationException>(() => default(AppKey) / "x");
    }

    [Fact]
    public void The_request_ledger_runs_a_request_once_and_keeps_the_newest()
    {
        var ledger = new Notes().Ledger;
        var id = Guid.NewGuid();
        Assert.False(ledger.Handled(id));
        Assert.True(ledger.Record(id));
        Assert.False(ledger.Record(id));
        Assert.True(ledger.Handled(id));

        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var small = new RequestLedger(ids, capacity: 3);
        Assert.Equal(ids.Skip(2), small.Ids);
        Assert.False(small.Handled(ids[0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestLedger([], 0));
    }

    [Fact]
    public void Present_publishes_both_views()
    {
        var app = new Notes();
        (string?, string?) shown = default;
        Assert.True(app.Show((side, main) => shown = (side, main), () => "side", () => "main"));
        Assert.Equal(("side", "main"), shown);
        Assert.Null(app.RecordedError);
    }

    [Fact]
    public void Present_records_the_error_state_when_a_view_throws_and_publishes_nothing()
    {
        var app = new Notes();
        var published = false;
        Assert.False(app.Show((_, _) => published = true, () => "side", () => throw new InvalidDataException("bad row")));
        Assert.False(published);
        var error = Assert.IsType<AppError>(app.RecordedError);
        Assert.Equal("Show", error.Operation);
        Assert.Equal(nameof(InvalidDataException), error.Kind);
        Assert.Equal("bad row", error.Message);
    }

    [Fact]
    public void RecordError_and_ClearError_set_the_error_state()
    {
        var app = new Notes();
        app.Fail(new IOException("disk"));
        Assert.Equal(new AppError("Fail", nameof(IOException), "disk"), app.RecordedError);
        app.Recover();
        Assert.Null(app.RecordedError);
    }

    [Fact]
    public void Swallow_keeps_the_reason_and_no_error_state()
    {
        var app = new Notes();
        app.Lose(new TimeoutException("slow"), "a late refresh is retried on the next tick");
        Assert.Null(app.RecordedError);
        var swallowed = Assert.Single(app.SwallowedErrors);
        Assert.Equal("a late refresh is retried on the next tick", swallowed.Reason);
        Assert.Throws<ArgumentException>(() => app.Lose(new TimeoutException(), " "));
        for (var i = 0; i < App.SwallowedKept + 10; i++) { app.Lose(new TimeoutException(), "again"); }
        Assert.Equal(App.SwallowedKept, app.SwallowedErrors.Count);
    }
}
