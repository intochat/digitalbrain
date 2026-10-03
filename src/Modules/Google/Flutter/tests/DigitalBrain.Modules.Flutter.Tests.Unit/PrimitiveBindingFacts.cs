using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Button.Signals;
using DigitalBrain.Flutter.FileInput;
using DigitalBrain.Flutter.Select;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Flutter.TextField.Signals;
using DigitalBrain.Flutter.VoiceInput;
using DigitalBrain.Flutter.VoiceInput.Signals;
using DigitalBrain.Kernel;
using DigitalBrain.Testing.Unit;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit;

public sealed class PrimitiveBindingFacts
{
    [Fact]
    public async Task UserEventsReachDurableHandlerWhileProjectionDoesNot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var handler = brain.Get<IPrimitiveTestHandler>("handler");
        var field = brain.Get<ITextField>("field");
        var binding = brain.Get<IUiBinding>("field");
        await binding.Bind(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => field.Configure("Message", "multiline", new string('x', 513)));
        await field.Configure("Message", "multiline", "workspace/app/send");
        await field.SetValue("projection");
        Assert.Empty(await handler.Events());
        await field.Input("first edit");
        await WaitFor(handler, 1, ct);
        Assert.Equal(new TextFieldChanged("field", "first edit"), Assert.Single(await handler.Events()));
        await brain.DeactivateAsync(binding, ct);
        await brain.DeactivateAsync(field, ct);
        await field.Input("second edit");
        await WaitFor(handler, 2, ct);
        Assert.Equal(new TextFieldChanged("field", "second edit"), (await handler.Events())[1]);
        Assert.Equal("second edit", (await field.Read()).Value);
        Assert.Equal("workspace/app/send", (await field.Read()).SubmitButton);
    }

    [Fact]
    public async Task SelectFileButtonAndVoiceDispatchTypedSignals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var handler = brain.Get<IPrimitiveTestHandler>("handler");
        foreach (var key in new[] { "select", "file", "button", "voice" }) { await brain.Get<IUiBinding>(key).Bind(handler); }
        var select = brain.Get<ISelect>("select");
        await select.Set("Model", [new("", "Automatic"), new("yes", "Ready"), new("no", "Unavailable", false)], "");
        await Assert.ThrowsAsync<ArgumentException>(() => select.Choose("no"));
        await Assert.ThrowsAsync<ArgumentException>(() => select.Choose("missing"));
        await select.Choose("yes");
        var file = brain.Get<IFileInput>("file");
        await file.Configure("Attach");
        await Assert.ThrowsAsync<ArgumentException>(() => file.Capture("large.txt", new string('x', 32_001)));
        await file.Capture("notes.txt", "notes");
        var button = brain.Get<IButton>("button");
        await button.Set("Send", "submit");
        await button.Click();
        var voice = brain.Get<IVoiceInput>("voice");
        await voice.Capture([1, 2], "audio/wav");
        await WaitFor(handler, 4, ct);
        var events = await handler.Events();
        Assert.Contains(events, signal => signal is SelectChanged { Name: "select", Value: "yes" });
        Assert.Contains(events, signal => signal is FileCaptured { Name: "file", FileName: "notes.txt", Content: "notes" });
        Assert.Contains(events, signal => signal is ButtonClicked { Name: "button", Action: "submit" });
        Assert.Contains(events, signal => signal is VoiceCaptured { Name: "voice", MimeType: "audio/wav" });
        await brain.DeactivateAsync(select, ct);
        Assert.Equal("yes", (await select.Read()).Selected);
        Assert.Equal(3, (await select.Read()).Options.Length);
    }

    [Fact]
    public async Task UnboundInputsWorkAndNavigationPayloadsAreValidated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        await brain.Get<ITextField>("field").Input("edit");
        await brain.Get<IFileInput>("file").Capture("empty.txt", "");
        var select = brain.Get<ISelect>("select");
        await select.Set("Choice", [new("a", "A")], "a");
        await select.Choose("a");
        var button = brain.Get<IButton>("open");
        await Assert.ThrowsAsync<ArgumentException>(() => button.SetActivation("Open", "[]"));
        await Assert.ThrowsAsync<ArgumentException>(() => button.SetActivation("Open", "invalid"));
        await button.SetActivation("Open", "{\"windowId\":\"table-1\"}");
        Assert.Equal("activate", (await button.Read()).Action);
        Assert.NotNull((await button.Read()).Activation);
        await brain.DeactivateAsync(button, ct);
        Assert.NotNull((await button.Read()).Activation);
        await button.Set("Send", "submit");
        Assert.Null((await button.Read()).Activation);
        await button.Click();
    }

    [Fact]
    public async Task HandlerCanProjectAnEditWithoutRedispatchingIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var handler = brain.Get<IPrimitiveTestHandler>("projecting-handler");
        var field = brain.Get<ITextField>("projection-field");
        await handler.ProjectTo("projection-field");
        await brain.Get<IUiBinding>("projection-field").Bind(handler);
        await field.Input("edited");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while ((await field.Read()).Value != "projected: edited") { await Task.Delay(20, timeout.Token); }
        Assert.Equal(new TextFieldChanged("projection-field", "edited"), Assert.Single(await handler.Events()));
        await field.SetValue("another projection");
        Assert.Single(await handler.Events());
    }
    private static async Task WaitFor(IPrimitiveTestHandler handler, int count, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while ((await handler.Events()).Count < count) { await Task.Delay(20, timeout.Token); }
    }
}

[Alias("test.primitive-handler"), Orleans.Metadata.DefaultGrainType("test.primitive-handler")]
public interface IPrimitiveTestHandler : IUiEventHandler
{
    Task<IReadOnlyList<Signal>> Events();
    Task ProjectTo(string name);
}

[GrainType("test.primitive-handler")]
public sealed class PrimitiveTestHandler : Grain, IPrimitiveTestHandler
{
    private readonly List<Signal> _events = [];
    private string? _projection;
    public Task ProjectTo(string name) { _projection = name; return Task.CompletedTask; }
    public async Task HandleUiEvent(Signal signal)
    {
        _events.Add(signal);
        if (_projection is not null && signal is TextFieldChanged changed)
        { await GrainFactory.GetGrain<ITextField>(_projection).SetValue("projected: " + changed.Value); }
    }
    public Task<IReadOnlyList<Signal>> Events() => Task.FromResult<IReadOnlyList<Signal>>(_events.ToArray());
}
