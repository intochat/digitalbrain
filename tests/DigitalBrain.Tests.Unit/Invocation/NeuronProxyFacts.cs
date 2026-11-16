// xUnit1051: the proxy facts pass their own tokens on purpose.
#pragma warning disable xUnit1051
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.Invocation;

public sealed class NeuronProxyFacts
{
    public sealed record Item(string Name, int Count);

    [NeuronAlias("tests.probe")]
    public interface IProbe
    {
        Task<int> Add(int value);
        [OneWayCall] Task Ping(string text);
        IAsyncEnumerable<Item> Items(CancellationToken cancellationToken);
        [CallTimeout(1000)] Task<int> Slow(CancellationToken cancellationToken);
        Task<Item> Find(string name);
        ValueTask Clear();
    }

    public interface IPlain
    {
        Task Go();
    }

    public abstract class NotAnInterface
    {
        public abstract Task Go();
    }

    private sealed class Sink : IInvocationSink
    {
        public List<(string Path, NeuronId Target, RuntimeInvocation Call)> Calls { get; } = [];
        public Func<RuntimeInvocation, CancellationToken, Task<JsonElement?>> Answer { get; set; } = (_, _) => Task.FromResult<JsonElement?>(null);

        public Task<JsonElement?> Invoke(NeuronId target, RuntimeInvocation call, CancellationToken cancellationToken)
        {
            Calls.Add(("invoke", target, call));
            return Answer(call, cancellationToken);
        }

        public Task Post(NeuronId target, RuntimeInvocation call)
        {
            Calls.Add(("post", target, call));
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<JsonElement> Stream(NeuronId target, RuntimeInvocation call, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Calls.Add(("stream", target, call));
            for (var i = 1; i <= 2; i++)
            {
                await Task.Yield();
                yield return JsonSerializer.SerializeToElement(new Item("i" + i, i), RuntimeProtocol.Json);
            }
        }
    }

    private static readonly NeuronId Id = NeuronId.Parse("b1/probe/a");

    [Fact]
    public async Task A_call_becomes_one_invocation_with_alias_key_method_and_json_arguments()
    {
        var sink = new Sink { Answer = (_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(3)) };
        var probe = NeuronProxy.Create<IProbe>(Id, sink);

        Assert.Equal(3, await probe.Add(2));

        var (path, target, call) = Assert.Single(sink.Calls);
        Assert.Equal("invoke", path);
        Assert.Equal(Id, target);
        Assert.Equal("tests.probe", call.Contract);
        Assert.Equal("b1/probe/a", call.Key);
        Assert.Equal("Add", call.Method);
        Assert.Equal(2, Assert.Single(call.Arguments).GetInt32());
        Assert.Equal(string.Empty, call.Behavior);
    }

    [Fact]
    public async Task Contract_without_alias_uses_its_full_name()
    {
        var sink = new Sink();
        await NeuronProxy.Create<IPlain>(Id, sink).Go();

        Assert.Equal(typeof(IPlain).FullName, Assert.Single(sink.Calls).Call.Contract);
    }

    [Fact]
    public async Task One_way_calls_post_and_streams_deserialize_items()
    {
        var sink = new Sink();
        var probe = NeuronProxy.Create<IProbe>(Id, sink);

        await probe.Ping("hi");
        Assert.Equal("post", Assert.Single(sink.Calls).Path);

        List<Item> items = [];
        await foreach (var item in probe.Items(CancellationToken.None)) { items.Add(item); }
        Assert.Equal([new Item("i1", 1), new Item("i2", 2)], items);
        Assert.Equal("stream", sink.Calls[1].Path);
        Assert.Empty(sink.Calls[1].Call.Arguments);
    }

    [Fact]
    public async Task Call_timeout_cancels_the_sink_call_and_throws_timeout()
    {
        var sink = new Sink
        {
            Answer = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return null; },
        };
        var probe = NeuronProxy.Create<IProbe>(Id, sink);

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<TimeoutException>(() => probe.Slow(CancellationToken.None));
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Caller_cancellation_stays_cancellation()
    {
        var sink = new Sink { Answer = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return null; } };
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NeuronProxy.Create<IProbe>(Id, sink).Slow(source.Token));
    }

    [Fact]
    public async Task Results_deserialize_to_the_declared_type_and_value_tasks_complete()
    {
        var sink = new Sink { Answer = (_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new Item("x", 7), RuntimeProtocol.Json)) };
        var probe = NeuronProxy.Create<IProbe>(Id, sink);

        Assert.Equal(new Item("x", 7), await probe.Find("x"));
        await probe.Clear();
        Assert.Equal(2, sink.Calls.Count);
    }

    [Fact]
    public void Non_interface_contracts_are_refused()
    {
        Assert.Throws<ArgumentException>(() => NeuronProxy.Create<NotAnInterface>(Id, new Sink()));
    }

    [Fact]
    public async Task Stream_cancellation_via_WithCancellation_propagates_to_sink_enumerator()
    {
        var sink = new EnumeratorTokenOnlySink();
        var probe = NeuronProxy.Create<IProbe>(Id, sink);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in probe.Items(CancellationToken.None).WithCancellation(cts.Token))
            {
                await cts.CancelAsync();
            }
        });

        Assert.True(sink.ObservedCancellation);
    }

    [Fact]
    public void ContractName_on_collectible_interface_does_not_pin_assembly()
    {
        var weak = ReadCollectibleContractName();
        for (var i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(weak.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReadCollectibleContractName()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CollectibleProxyContract"), AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Main");
        var contract = module.DefineType("ICollectibleProbe", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        contract.DefineMethod("Ping", MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual, typeof(Task<int>), Type.EmptyTypes);
        var interfaceType = contract.CreateType();

        Assert.True(interfaceType.IsCollectible);
        Assert.Equal("ICollectibleProbe", NeuronProxy.ContractName(interfaceType));

        return new WeakReference(assembly, trackResurrection: true);
    }

    private sealed class EnumeratorTokenOnlySink : IInvocationSink
    {
        public bool ObservedCancellation { get; private set; }

        public Task<JsonElement?> Invoke(NeuronId target, RuntimeInvocation call, CancellationToken cancellationToken)
            => Task.FromResult<JsonElement?>(null);

        public Task Post(NeuronId target, RuntimeInvocation call) => Task.CompletedTask;

        public IAsyncEnumerable<JsonElement> Stream(NeuronId target, RuntimeInvocation call, CancellationToken cancellationToken)
            => new UnboundEnumerable(this);

        private sealed class UnboundEnumerable(EnumeratorTokenOnlySink owner) : IAsyncEnumerable<JsonElement>
        {
            public IAsyncEnumerator<JsonElement> GetAsyncEnumerator(CancellationToken cancellationToken = default)
                => new UnboundEnumerator(owner, cancellationToken);
        }

        private sealed class UnboundEnumerator(EnumeratorTokenOnlySink owner, CancellationToken cancellationToken) : IAsyncEnumerator<JsonElement>
        {
            private int _step;

            public JsonElement Current => JsonSerializer.SerializeToElement(new NeuronProxyFacts.Item("x", _step), RuntimeProtocol.Json);

            public ValueTask<bool> MoveNextAsync()
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    owner.ObservedCancellation = true;
                    return ValueTask.FromException<bool>(new OperationCanceledException(cancellationToken));
                }

                _step++;
                return ValueTask.FromResult(true);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
