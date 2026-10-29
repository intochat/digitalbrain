// CA1068: Tokened deliberately takes tokens first and last (token injection anywhere). xUnit1051: the binder facts pass their own tokens on purpose. CA1861: inline sample arguments.
#pragma warning disable CA1068, xUnit1051, CA1861
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Invocation;

namespace DigitalBrain.Tests.Unit.Invocation;

public sealed class NeuronBinderFacts
{
    public sealed record Item(string Name, int Count);

    public interface IProbe : IDisposable
    {
        Task<int> Add(int value);
        Task<string> Add(string value);
        Task<int> Twice(int value, CancellationToken cancellationToken);
        Task<string> Tokened(CancellationToken first, string text, CancellationToken second);
        void Touch();
        Task Reset();
        ValueTask Clear();
        ValueTask<Item> Find(string name);
        IAsyncEnumerable<int> Count(int upTo, CancellationToken cancellationToken);
        IAsyncEnumerable<int> Forever(CancellationToken cancellationToken);
        Task<long> Wide(long value);
        Task<double> Wide(double value);
        Task Fail();
        Task Put(Item item);
    }

    public sealed class Probe : IProbe
    {
        public int Touched { get; private set; }
        public CancellationToken? First { get; private set; }
        public CancellationToken? Second { get; private set; }

        public Task<int> Add(int value) => Task.FromResult(value + 1);
        public Task<string> Add(string value) => Task.FromResult(value + "!");
        public Task<int> Twice(int value, CancellationToken cancellationToken) { First = cancellationToken; return Task.FromResult(value * 2); }
        public Task<string> Tokened(CancellationToken first, string text, CancellationToken second) { First = first; Second = second; return Task.FromResult(text); }
        public void Touch() => Touched++;
        public Task Reset() => Task.CompletedTask;
        public ValueTask Clear() => ValueTask.CompletedTask;
        public async ValueTask<Item> Find(string name) { await Task.Yield(); return new Item(name, 3); }
        public async IAsyncEnumerable<int> Count(int upTo, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 1; i <= upTo; i++) { await Task.Yield(); yield return i; }
        }
        public async IAsyncEnumerable<int> Forever([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; ; i++) { await Task.Delay(1, cancellationToken); yield return i; }
        }
        public Task<long> Wide(long value) => Task.FromResult(value);
        public Task<double> Wide(double value) => Task.FromResult(value);
        public Task Fail() => throw new InvalidTimeZoneException("boom");
        public Task Put(Item item) => Task.CompletedTask;
        public void Dispose() { }
    }

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, RuntimeProtocol.Json);

    private static RuntimeInvocation Call(string method, params JsonElement[] arguments)
        => new("behavior", typeof(IProbe).FullName!, "b1/probe/a", method, arguments);

    private static async Task<JsonElement?> Run(NeuronBinder binder, object target, RuntimeInvocation call, CancellationToken token = default)
        => await binder.InvokeAsync(target, binder.Bind(typeof(IProbe), call), call.Arguments, token);

    [Fact]
    public async Task Overloads_bind_by_parameter_type_and_ambiguity_names_contract_and_method()
    {
        var binder = new NeuronBinder();
        var probe = new Probe();

        Assert.Equal(3, (await Run(binder, probe, Call("Add", Json(2))))!.Value.GetInt32());
        Assert.Equal("a!", (await Run(binder, probe, Call("Add", Json("a"))))!.Value.GetString());

        var error = Assert.Throws<BindingException>(() => binder.Bind(typeof(IProbe), Call("Wide", Json(2))));
        Assert.Contains("IProbe", error.Message, StringComparison.Ordinal);
        Assert.Contains("Wide", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_method_arity_malformed_and_oversized_arguments_raise_binding_errors()
    {
        var binder = new NeuronBinder(maximumArgumentBytes: 64);
        var probe = new Probe();

        Assert.Throws<BindingException>(() => binder.Bind(typeof(IProbe), Call("Nope")));
        Assert.Throws<BindingException>(() => binder.Bind(typeof(IProbe), Call("Reset", Json(1))));
        Assert.Throws<BindingException>(() => binder.Bind(typeof(IProbe), Call("Add", Json(new[] { 1, 2 }))));
        await Assert.ThrowsAsync<BindingException>(() => Run(binder, probe, Call("Find", Json(5))));
        Assert.Throws<BindingException>(() => binder.Bind(typeof(IProbe), Call("Find", Json(new string('x', 100)))));
        Assert.Throws<BindingException>(() => binder.Bind(typeof(IProbe), null!));
    }

    [Fact]
    public void Object_and_disposal_members_are_not_bindable()
    {
        var binder = new NeuronBinder();

        Assert.Throws<BindingException>(() => binder.Bind(typeof(IProbe), Call("Dispose")));
        Assert.Throws<BindingException>(() => binder.Bind(typeof(IProbe), Call("GetHashCode")));
        Assert.Throws<BindingException>(() => binder.Bind(typeof(Probe), Call("ToString")));
        Assert.Throws<BindingException>(() => binder.Bind(typeof(Probe), Call("Dispose")));
    }

    [Fact]
    public async Task Cancellation_tokens_receive_the_caller_token_wherever_they_appear()
    {
        var binder = new NeuronBinder();
        var probe = new Probe();
        using var source = new CancellationTokenSource();

        Assert.Equal(8, (await Run(binder, probe, Call("Twice", Json(4)), source.Token))!.Value.GetInt32());
        Assert.Equal(source.Token, probe.First);
        Assert.Equal("t", (await Run(binder, probe, Call("Tokened", Json("t")), source.Token))!.Value.GetString());
        Assert.Equal(source.Token, probe.First);
        Assert.Equal(source.Token, probe.Second);
    }

    [Fact]
    public async Task Every_result_shape_returns_json_and_streams_stop_on_cancel()
    {
        var binder = new NeuronBinder();
        var probe = new Probe();

        Assert.Null(await Run(binder, probe, Call("Touch")));
        Assert.Equal(1, probe.Touched);
        Assert.Null(await Run(binder, probe, Call("Reset")));
        Assert.Null(await Run(binder, probe, Call("Clear")));
        Assert.Equal(new Item("n", 3), (await Run(binder, probe, Call("Find", Json("n"))))!.Value.Deserialize<Item>(RuntimeProtocol.Json));

        var count = Call("Count", Json(3));
        List<int> items = [];
        await foreach (var item in binder.StreamAsync(probe, binder.Bind(typeof(IProbe), count), count.Arguments, CancellationToken.None))
        {
            items.Add(item.GetInt32());
        }
        Assert.Equal([1, 2, 3], items);

        var forever = Call("Forever");
        using var source = new CancellationTokenSource();
        var seen = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in binder.StreamAsync(probe, binder.Bind(typeof(IProbe), forever), forever.Arguments, source.Token))
            {
                if (++seen == 2) { await source.CancelAsync(); }
            }
        });
        Assert.Equal(2, seen);
    }

    [Fact]
    public async Task Target_exceptions_surface_as_their_original_type()
    {
        await Assert.ThrowsAsync<InvalidTimeZoneException>(() => Run(new NeuronBinder(), new Probe(), Call("Fail")));
    }

    [Fact]
    public void Second_bind_is_a_cache_hit()
    {
        var binder = new NeuronBinder();

        var first = binder.Bind(typeof(IProbe), Call("Twice", Json(1)));
        var misses = binder.Misses;
        var second = binder.Bind(typeof(IProbe), Call("Twice", Json(5)));

        Assert.Equal(1, misses);
        Assert.Equal(misses, binder.Misses);
        Assert.Same(first, second);
    }

    [Fact]
    public void Encoder_matches_the_os_proxy_encoding()
    {
        // Golden strings: os IntoChat.OS.Client NeuronProxy serializes each non-token argument with its parameter type
        // under RuntimeProtocol.Json (Web defaults, MaxDepth 128).
        static string Encoded(string method, params object?[] args)
            => string.Join("|", InvocationEncoder.Encode(typeof(IProbe).GetMethods().First(m => m.Name == method), args).Select(e => e.GetRawText()));

        Assert.Equal("2", Encoded("Twice", 2, CancellationToken.None));
        Assert.Equal("\"t\"", Encoded("Tokened", CancellationToken.None, "t", CancellationToken.None));
        Assert.Equal("{\"name\":\"x\",\"count\":3}", Encoded("Put", new Item("x", 3)));
    }
}
