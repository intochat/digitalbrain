using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Access;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class CallCatalogFacts
{
    public sealed record SampleRecord(string Key, int Value);

    public interface IBaseService
    {
        Task<string> BaseMethod(string input);
    }

    public interface ICalculatorService : IBaseService, IDisposable
    {
        void Reset();
        Task Clear();
        ValueTask Flush();
        int SynchronousAdd(int a, int b);
        Task<int> Add(int a, int b);
        Task<string> Add(string prefix, string suffix);
        Task<int> MultiplyWithToken(int factor, CancellationToken ct);
        ValueTask<SampleRecord> FetchRecord(string key);
        Task ThrowDomainError();
        IAsyncEnumerable<int> StreamItems(int count, CancellationToken ct);
    }

    public class CalculatorImplementation : ICalculatorService
    {
        public bool WasReset { get; private set; }
        public bool WasCleared { get; private set; }
        public bool WasFlushed { get; private set; }
        public CancellationToken CapturedToken { get; private set; }

        public void Reset() => WasReset = true;
        public Task Clear() { WasCleared = true; return Task.CompletedTask; }
        public ValueTask Flush() { WasFlushed = true; return ValueTask.CompletedTask; }
        public int SynchronousAdd(int a, int b) => a + b;
        public Task<int> Add(int a, int b) => Task.FromResult(a + b);
        public Task<string> Add(string prefix, string suffix) => Task.FromResult(prefix + suffix);
        public Task<int> MultiplyWithToken(int factor, CancellationToken ct)
        {
            CapturedToken = ct;
            return Task.FromResult(factor * 10);
        }

        public ValueTask<SampleRecord> FetchRecord(string key) => ValueTask.FromResult(new SampleRecord(key, 42));
        public Task ThrowDomainError() => throw new ArgumentException("Custom domain invalid parameter");

        public async IAsyncEnumerable<int> StreamItems(int count, [EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 1; i <= count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }

        public Task<string> BaseMethod(string input) => Task.FromResult("base:" + input);
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }

    [Fact]
    public void Build_indexes_contract_methods_and_excludes_disposable()
    {
        var catalog = CallCatalog.Build(typeof(ICalculatorService));

        Assert.Equal(typeof(ICalculatorService), catalog.TargetType);
        Assert.True(catalog.TryResolve(nameof(ICalculatorService.Add), arity: 2, out var addDesc));
        Assert.NotNull(addDesc);

        // Inherited interface method from IBaseService is discovered
        Assert.True(catalog.TryResolve(nameof(IBaseService.BaseMethod), arity: 1, out var baseDesc));
        Assert.NotNull(baseDesc);

        // IDisposable.Dispose is excluded
        Assert.False(catalog.TryResolve("Dispose", arity: 0, out _));
    }

    [Fact]
    public void Arity_excludes_cancellation_token_parameters()
    {
        var catalog = CallCatalog.Build(typeof(ICalculatorService));

        // MultiplyWithToken takes (int factor, CancellationToken ct) -> non-token Arity is 1
        Assert.True(catalog.TryResolve(nameof(ICalculatorService.MultiplyWithToken), arity: 1, out var desc));
        Assert.NotNull(desc);
        Assert.Equal(1, desc.Arity);
        Assert.Equal(2, desc.ParameterTypes.Length);
        Assert.Equal(typeof(int), desc.ParameterTypes[0]);
        Assert.Equal(typeof(CancellationToken), desc.ParameterTypes[1]);
    }

    [Fact]
    public async Task Precompiled_invoker_executes_all_return_shapes_in_O1()
    {
        var catalog = CallCatalog.Build(typeof(ICalculatorService));
        var calc = new CalculatorImplementation();

        // 1. void
        var resetDesc = catalog.Resolve(nameof(ICalculatorService.Reset), arity: 0);
        var resetResult = await resetDesc.Invoker(calc, []);
        Assert.Null(resetResult);
        Assert.True(calc.WasReset);

        // 2. Task
        var clearDesc = catalog.Resolve(nameof(ICalculatorService.Clear), arity: 0);
        var clearResult = await clearDesc.Invoker(calc, []);
        Assert.Null(clearResult);
        Assert.True(calc.WasCleared);

        // 3. ValueTask
        var flushDesc = catalog.Resolve(nameof(ICalculatorService.Flush), arity: 0);
        var flushResult = await flushDesc.Invoker(calc, []);
        Assert.Null(flushResult);
        Assert.True(calc.WasFlushed);

        // 4. Synchronous int
        var syncDesc = catalog.Resolve(nameof(ICalculatorService.SynchronousAdd), arity: 2);
        var syncResult = await syncDesc.Invoker(calc, [3, 4]);
        Assert.Equal(7, syncResult);

        // 5. Task<int>
        var addDesc = catalog.Resolve(nameof(ICalculatorService.Add), arity: 2);
        var addResult = await addDesc.Invoker(calc, [10, 20]);
        Assert.Equal(30, addResult);

        // 6. ValueTask<SampleRecord>
        var fetchDesc = catalog.Resolve(nameof(ICalculatorService.FetchRecord), arity: 1);
        var fetchResult = await fetchDesc.Invoker(calc, ["item-A"]);
        Assert.Equal(new SampleRecord("item-A", 42), fetchResult);
    }

    [Fact]
    public async Task Precompiled_invoker_passes_cancellation_token_correctly()
    {
        var catalog = CallCatalog.Build(typeof(ICalculatorService));
        var calc = new CalculatorImplementation();
        using var cts = new CancellationTokenSource();

        var desc = catalog.Resolve(nameof(ICalculatorService.MultiplyWithToken), arity: 1);

        // Called with explicit token
        var resultWithToken = await desc.Invoker(calc, [5, cts.Token]);
        Assert.Equal(50, resultWithToken);
        Assert.Equal(cts.Token, calc.CapturedToken);

        // Called with arity only (token omitted -> defaults to CancellationToken.None)
        var resultOmitted = await desc.Invoker(calc, [7]);
        Assert.Equal(70, resultOmitted);
        Assert.Equal(CancellationToken.None, calc.CapturedToken);
    }

    [Fact]
    public async Task Precompiled_invoker_surfaces_target_exception_directly()
    {
        var catalog = CallCatalog.Build(typeof(ICalculatorService));
        var calc = new CalculatorImplementation();

        var desc = catalog.Resolve(nameof(ICalculatorService.ThrowDomainError), arity: 0);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => desc.Invoker(calc, []));
        Assert.Equal("Custom domain invalid parameter", ex.Message);
    }

    [Fact]
    public void Overloads_disambiguate_via_JSON_argument_types()
    {
        var catalog = CallCatalog.Build(typeof(ICalculatorService));

        var intCall = catalog.Resolve(nameof(ICalculatorService.Add), [JsonSerializer.SerializeToElement(1), JsonSerializer.SerializeToElement(2)]);
        Assert.Equal(typeof(int), intCall.ParameterTypes[0]);

        var stringCall = catalog.Resolve(nameof(ICalculatorService.Add), [JsonSerializer.SerializeToElement("a"), JsonSerializer.SerializeToElement("b")]);
        Assert.Equal(typeof(string), stringCall.ParameterTypes[0]);
    }

    [Fact]
    public void GetOrCreate_caches_catalogs_immutably()
    {
        var cat1 = CallCatalog.GetOrCreate(typeof(ICalculatorService));
        var cat2 = CallCatalog.GetOrCreate(typeof(ICalculatorService));

        Assert.Same(cat1, cat2);
    }

    [Fact]
    public void BuildForNeuron_indexes_all_implemented_contracts()
    {
        var catalog = CallCatalog.BuildForNeuron(typeof(CalculatorImplementation));

        Assert.True(catalog.TryResolve(nameof(ICalculatorService.Add), arity: 2, out _));
        Assert.True(catalog.TryResolve(nameof(IBaseService.BaseMethod), arity: 1, out _));
    }
}
