using DigitalBrain.InoLang;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.InoLang.Tests;

/// <summary>
/// Elegant, unified testing standard for DigitalBrain neurons.
/// Works for both AST-level in-memory UnitTests and Silo-level IntegrationTests.
/// </summary>
public static class NeuronTest
{
    public static UnitTestBuilder UnitTest() => new();
    
    public static IntegrationTestBuilder IntegrationTest() => new();

    public static UnitTestBuilder For(string source) => new UnitTestBuilder().WithSource(source);

    public static IntegrationTestBuilder Integration(TestDigitalBrain brain) => new IntegrationTestBuilder().WithBrain(brain);
}

/// <summary>
/// Exception thrown when a fluent neuron test assertion fails.
/// </summary>
public sealed class NeuronTestAssertionException : Exception
{
    public NeuronTestAssertionException(string message) : base(message)
    {
    }
}

/// <summary>
/// Programmatically compiles, runs, and asserts against `.ino` files in-memory using AST interpretation.
/// Runs in milliseconds with zero Orleans cluster booting overhead.
/// </summary>
public sealed class UnitTestBuilder
{
    private string? _source;
    private IContractCatalog? _catalog;
    private readonly StubNeuronHost _neurons = new();
    private string? _whenSynapsePort;
    private readonly Dictionary<string, string> _inbound = new(StringComparer.Ordinal);
    private readonly List<Action<ActivationResult>> _assertions = new();
    private readonly Dictionary<string, string> _memory = new(StringComparer.Ordinal);

    public UnitTestBuilder ForNeuron(string source)
    {
        _source = source;
        return this;
    }

    public UnitTestBuilder WithSource(string source)
    {
        _source = source;
        return this;
    }

    public UnitTestBuilder WithCatalog(IContractCatalog catalog)
    {
        _catalog = catalog;
        return this;
    }

    public UnitTestBuilder GivenPredicate(string builtin, string value)
    {
        _neurons.PredicateValues[builtin] = value;
        return this;
    }

    public UnitTestBuilder GivenNeuron(string port, string returns)
    {
        _neurons.NeuronReturns[port] = returns;
        return this;
    }

    public UnitTestBuilder GivenMemory(string key, string value)
    {
        _memory[key] = value;
        return this;
    }

    public UnitTestBuilder Given(string key, string value)
    {
        _neurons.PredicateValues[key] = value;
        _neurons.NeuronReturns[key] = value;
        return this;
    }

    public UnitTestBuilder When(string port, params (string Key, string Value)[] args)
    {
        return WhenSynapse(port, args);
    }

    public UnitTestBuilder Expect(string key, string value)
    {
        _assertions.Add(res =>
        {
            var hasResource = res.SavedResources.TryGetValue(key, out var resVal);
            var sig = res.EmittedSynapses.FirstOrDefault(s => s.Port == key);
            
            if (!hasResource && sig is null)
            {
                throw new NeuronTestAssertionException($"Expected resource or signal '{key}' was not found.");
            }
            
            if (hasResource && resVal != value)
            {
                throw new NeuronTestAssertionException($"Expected resource '~{key}' to have value '{value}' but was '{resVal ?? "<null>"}'.");
            }
            
            if (sig is not null)
            {
                var match = sig.Args.Values.Contains(value);
                if (!match)
                {
                    throw new NeuronTestAssertionException($"Expected signal '{key}' to contain value '{value}' but did not match.");
                }
            }
        });
        return this;
    }

    public UnitTestBuilder Expect(string port, string field, string expectedValue)
    {
        return ExpectSignal(port, field, expectedValue);
    }

    public UnitTestBuilder WhenSynapse(string port, Dictionary<string, string> args)
    {
        _whenSynapsePort = port;
        foreach (var (k, v) in args)
        {
            _inbound[k] = v;
        }
        return this;
    }

    public UnitTestBuilder WhenSynapse(string port, params (string Key, string Value)[] args)
    {
        _whenSynapsePort = port;
        foreach (var arg in args)
        {
            _inbound[arg.Key] = arg.Value;
        }
        return this;
    }

    public UnitTestBuilder ExpectSignal(string port, Func<IReadOnlyDictionary<string, string>, bool> predicate)
    {
        _assertions.Add(res =>
        {
            var sig = res.EmittedSynapses.FirstOrDefault(s => s.Port == port);
            if (sig is null)
                throw new NeuronTestAssertionException($"Expected signal '!{port}' was not emitted.");
            if (!predicate(sig.Args))
                throw new NeuronTestAssertionException($"Emitted signal '!{port}' did not match the expected predicate.");
        });
        return this;
    }

    public UnitTestBuilder ExpectSignal(string port, string field, string expectedValue)
    {
        return ExpectSignal(port, args => args.TryGetValue(field, out var act) && act == expectedValue);
    }

    public UnitTestBuilder ExpectResource(string port, string expectedValue)
    {
        _assertions.Add(res =>
        {
            var act = res.SavedResources.GetValueOrDefault(port);
            if (act != expectedValue)
                throw new NeuronTestAssertionException($"Expected resource '~{port}' to have value '{expectedValue}' but was '{act ?? "<null>"}'.");
        });
        return this;
    }

    public UnitTestBuilder ExpectCounter(string counter, long expectedValue)
    {
        _assertions.Add(res =>
        {
            var act = res.Counters.GetValueOrDefault(counter, 0);
            if (act != expectedValue)
                throw new NeuronTestAssertionException($"Expected counter '{counter}' to be {expectedValue} but was {act}.");
        });
        return this;
    }

    /// <summary>
    /// Evaluates and runs the programmatic in-memory unit test.
    /// </summary>
    public async Task<ActivationResult> RunAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_source))
            throw new InvalidOperationException("InoLang source was not specified.");

        var catalog = _catalog ?? DeferredContractCatalog.Instance;
        var compiled = InoCompiler.Compile(_source, catalog);
        if (!compiled.Success)
        {
            var errors = string.Join(" | ", compiled.Diagnostics.Select(d => $"{d.Code} {d.Message}"));
            throw new InvalidOperationException($"Compilation failed: {errors}");
        }

        if (string.IsNullOrEmpty(_whenSynapsePort))
            throw new InvalidOperationException("WhenSynapse was not specified.");

        var interpreter = new Interpreter(compiled.Plan!);
        var result = await interpreter.RunAsync(TriggerKey.Port(_whenSynapsePort), _inbound, _neurons, _memory, ct);

        foreach (var assert in _assertions)
        {
            assert(result);
        }

        return result;
    }

    /// <summary>
    /// Executes all compiled BDD scenarios defined in the .ino source directly.
    /// </summary>
    public async Task RunScenariosAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_source))
            throw new InvalidOperationException("InoLang source was not specified.");

        var catalog = _catalog ?? DeferredContractCatalog.Instance;
        var compiled = InoCompiler.Compile(_source, catalog);
        if (!compiled.Success)
        {
            var errors = string.Join(" | ", compiled.Diagnostics.Select(d => $"{d.Code} {d.Message}"));
            throw new InvalidOperationException($"Compilation failed: {errors}");
        }

        var runner = new ScenarioRunner();
        var report = await runner.RunAllAsync(compiled.Plan!, ct);
        if (!report.AllPassed)
        {
            var failures = string.Join(" | ", report.Results.SelectMany(r => r.Failures));
            throw new NeuronTestAssertionException($"Scenario run failed: {failures}");
        }
    }
}

/// <summary>
/// Programmatically orchestrates and asserts against a running Orleans TestDigitalBrain sandbox.
/// Automatically handles correlation IDs, sequential awaits, timeouts, and structured custom assertions.
/// </summary>
public sealed class IntegrationTestBuilder
{
    private TestDigitalBrain? _brain;
    private Func<Guid, Synapse>? _emitFactory;
    private readonly List<Func<Synapse, bool>> _expects = new();
    private readonly List<Type> _expectTypes = new();
    private TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public IntegrationTestBuilder WithBrain(TestDigitalBrain brain)
    {
        _brain = brain;
        return this;
    }

    public IntegrationTestBuilder WhenEmit(Func<Guid, Synapse> factory)
    {
        _emitFactory = factory;
        return this;
    }

    public IntegrationTestBuilder ExpectSynapse<TSynapse>(Func<TSynapse, bool> predicate) where TSynapse : Synapse
    {
        _expectTypes.Add(typeof(TSynapse));
        _expects.Add(syn => syn is TSynapse expected && predicate(expected));
        return this;
    }

    public IntegrationTestBuilder When(Func<Guid, Synapse> factory)
    {
        return WhenEmit(factory);
    }

    public IntegrationTestBuilder Expect<TSynapse>(Func<TSynapse, bool> predicate) where TSynapse : Synapse
    {
        return ExpectSynapse(predicate);
    }

    public IntegrationTestBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Boots the synapse trigger and runs the integration test.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (_brain is null)
            throw new InvalidOperationException("Brain was not specified.");
        if (_emitFactory is null)
            throw new InvalidOperationException("Synapse factory to emit was not specified.");

        var correlationId = Guid.NewGuid();
        var request = _emitFactory(correlationId);

        await _brain.Emit(request, ct);

        for (int i = 0; i < _expectTypes.Count; i++)
        {
            var expectType = _expectTypes[i];
            var predicate = _expects[i];

            var method = typeof(TestDigitalBrain)
                .GetMethod(nameof(TestDigitalBrain.AwaitSynapse))
                ?.MakeGenericMethod(expectType);

            if (method is null)
                throw new InvalidOperationException($"Could not find generic method AwaitSynapse on TestDigitalBrain for type {expectType.Name}.");

            var task = (Task)method.Invoke(_brain, [correlationId, _timeout, ct])!;
            await task;

            var resultProperty = task.GetType().GetProperty("Result");
            var result = (Synapse)resultProperty?.GetValue(task)!;

            if (!predicate(result))
            {
                throw new NeuronTestAssertionException($"Awaited synapse of type '{expectType.Name}' did not match the expected predicate.");
            }
        }
    }
}
