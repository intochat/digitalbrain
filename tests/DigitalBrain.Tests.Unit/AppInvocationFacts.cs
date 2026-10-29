#pragma warning disable CA1822, DB0014
using System.Text.Json;

namespace DigitalBrain.Tests.Unit;

public sealed class AppInvocationFacts
{
    [Fact]
    public void Creation_uses_widest_constructor_services_and_optional_defaults()
    {
        var service = new Service();
        var app = (ConstructedApp)new AppDefinition(typeof(ConstructedApp)).Create(t => t == typeof(Service) ? service : null);
        Assert.Same(service, app.Service);
        Assert.Equal(7, app.Number);
        Assert.Throws<InvalidOperationException>(() => new AppDefinition(typeof(ConstructedApp)).Create(_ => null));
        var error = Assert.Throws<InvalidOperationException>(() => new AppDefinition(typeof(AmbiguousApp)).Create(_ => null));
        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invocation_preserves_objects_converts_json_injects_token_and_awaits_completion()
    {
        var definition = new AppDefinition(typeof(DispatchApp));
        var app = (DispatchApp)definition.Create(_ => null);
        var input = new Input(3);
        await definition.Invoke(app, "s-E-t", input, ct: TestContext.Current.CancellationToken);
        Assert.Same(input, app.Input);
        await definition.Invoke(app, "Set", new { value = 4 }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(4, app.Input!.Value);
        await definition.Invoke(app, "Set", null, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, app.Input!.Value);
        using var cancellation = new CancellationTokenSource();
        await definition.Invoke(app, "TokenOnly", null, ct: cancellation.Token);
        Assert.Equal(cancellation.Token, app.Token);
        var pending = definition.Invoke(app, "Wait", null, ct: TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        app.Completion.SetResult();
        await pending;
        Assert.True(app.Done);
        await definition.Invoke(app, "Value", null, ct: TestContext.Current.CancellationToken);
        Assert.True(app.ValueDone);
        await definition.Invoke(app, "Result", null, ct: TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => definition.Invoke(app, "missing", null, ct: TestContext.Current.CancellationToken).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Invocation_uses_caller_json_and_propagates_original_exceptions()
    {
        var definition = new AppDefinition(typeof(DispatchApp));
        var app = (DispatchApp)definition.Create(_ => null);
        await definition.Invoke(app, "Set", new { VALUE = 9 }, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, TestContext.Current.CancellationToken);
        Assert.Equal(9, app.Input!.Value);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => definition.Invoke(app, "Fail", null, ct: TestContext.Current.CancellationToken));
        Assert.Same(app.Error, error);
        Assert.Throws<InvalidOperationException>(() => new AppDefinition(typeof(ThrowingApp)).Create(_ => null));
    }

    public sealed class Service;
    public sealed record Input(int Value);
    [App("constructed")]
    public sealed class ConstructedApp : App
    {
        public ConstructedApp() { }
        public ConstructedApp(Service service, int number = 7) { Service = service; Number = number; }
        public Service? Service { get; }
        public int Number { get; }
    }
    [App("ambiguous")]
    public sealed class AmbiguousApp : App
    {
        public AmbiguousApp(string value) { }
        public AmbiguousApp(Service value) { }
    }
    [App("throwing")]
    public sealed class ThrowingApp : App
    {
        public ThrowingApp() => throw new InvalidOperationException("constructor");
    }
    [App("dispatch")]
    public sealed class DispatchApp : App
    {
        public Input? Input { get; private set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource Completion { get; } = new();
        public bool Done { get; private set; }
        public bool ValueDone { get; private set; }
        public InvalidOperationException Error { get; } = new("original");
        [On] public void Set(Input input) => Input = input;
        [On] public void TokenOnly(CancellationToken ct) => Token = ct;
        [On] public async Task Wait() { await Completion.Task; Done = true; }
        [On] public async ValueTask Value() { await Task.Yield(); ValueDone = true; }
        [On] public Task<int> Result() => Task.FromResult(2);
        [On] public void Fail() => throw Error;
    }
}
