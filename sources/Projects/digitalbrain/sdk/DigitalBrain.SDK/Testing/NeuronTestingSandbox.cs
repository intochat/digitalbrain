using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Planning;
using DigitalBrain.InoLang.Runtime;

namespace DigitalBrain.SDK.Testing;

public sealed class SandboxResult
{
    public bool Success { get; }
    public IReadOnlyList<string> Logs { get; }
    public IReadOnlyList<EmittedSynapse> EmittedSynapses { get; }
    public IReadOnlyDictionary<string, string> SavedResources { get; }
    public IReadOnlyDictionary<string, long> Counters { get; }
    public string? ErrorMessage { get; }

    public SandboxResult(
        bool success,
        IReadOnlyList<string> logs,
        IReadOnlyList<EmittedSynapse> emitted,
        IReadOnlyDictionary<string, string> resources,
        IReadOnlyDictionary<string, long> counters,
        string? errorMessage = null)
    {
        Success = success;
        Logs = logs;
        EmittedSynapses = emitted;
        SavedResources = resources;
        Counters = counters;
        ErrorMessage = errorMessage;
    }
}

public sealed class SandboxNeuronMockHost : INeuronHost
{
    private readonly Dictionary<string, string> _mocks;
    private readonly Dictionary<(string Builtin, string Subject), string> _predicateMocks;

    public SandboxNeuronMockHost(
        Dictionary<string, string> mocks,
        Dictionary<(string Builtin, string Subject), string> predicateMocks)
    {
        _mocks = mocks;
        _predicateMocks = predicateMocks;
    }

    public Task<string> AskAsync(string port, string prompt, CancellationToken ct)
    {
        if (_mocks.TryGetValue(port, out var response))
        {
            return Task.FromResult(response);
        }
        return Task.FromResult("");
    }

    public Task<bool> EvaluatePredicateAsync(string builtin, string subject, string target, CancellationToken ct)
    {
        if (_predicateMocks.TryGetValue((builtin, subject), out var value))
        {
            return Task.FromResult(string.Equals(value, target, StringComparison.OrdinalIgnoreCase));
        }

        // Fallback for matches by just builtin
        var keyByBuiltin = _predicateMocks.Keys.FirstOrDefault(k => string.Equals(k.Builtin, builtin, StringComparison.OrdinalIgnoreCase));
        if (keyByBuiltin != default)
        {
            var valueByBuiltin = _predicateMocks[keyByBuiltin];
            return Task.FromResult(string.Equals(valueByBuiltin, target, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(true);
    }
}

public sealed class NeuronTestingSandbox
{
    private readonly string _source;
    private readonly Dictionary<string, string> _neuronMocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Builtin, string Subject), string> _predicateMocks = new();

    public NeuronTestingSandbox(string source)
    {
        _source = source;
    }

    public NeuronTestingSandbox StubNeuron(string portOrFqn, string returnValue)
    {
        _neuronMocks[portOrFqn] = returnValue;
        return this;
    }

    public NeuronTestingSandbox StubPredicate(string builtin, string subject, string returnValue)
    {
        _predicateMocks[(builtin, subject)] = returnValue;
        return this;
    }

    public async Task<SandboxResult> RunScenarioAsync(CancellationToken ct = default)
    {
        var compiled = InoCompiler.Compile(_source);
        if (!compiled.Success)
        {
            var errors = string.Join("\n", compiled.Diagnostics.Select(d => d.Message));
            return new SandboxResult(false, Array.Empty<string>(), Array.Empty<EmittedSynapse>(), new Dictionary<string, string>(), new Dictionary<string, long>(), $"Compilation failed: {errors}");
        }

        var gate = await compiled.EvaluateGateAsync(ct);
        if (!gate.CanActivate)
        {
            return new SandboxResult(false, Array.Empty<string>(), Array.Empty<EmittedSynapse>(), new Dictionary<string, string>(), new Dictionary<string, long>(), $"Gate validation failed: {gate.Reason}");
        }

        return new SandboxResult(true, Array.Empty<string>(), Array.Empty<EmittedSynapse>(), new Dictionary<string, string>(), new Dictionary<string, long>());
    }

    public async Task<SandboxResult> InjectSynapseAsync(string triggerPort, Dictionary<string, string> args, CancellationToken ct = default)
    {
        var compiled = InoCompiler.Compile(_source);
        if (!compiled.Success || compiled.Plan == null)
        {
            var errors = string.Join("\n", compiled.Diagnostics.Select(d => d.Message));
            return new SandboxResult(false, Array.Empty<string>(), Array.Empty<EmittedSynapse>(), new Dictionary<string, string>(), new Dictionary<string, long>(), $"Compilation failed: {errors}");
        }

        var mockHost = new SandboxNeuronMockHost(_neuronMocks, _predicateMocks);
        var interpreter = new Interpreter(compiled.Plan);
        try
        {
            var runResult = await interpreter.RunAsync(TriggerKey.Port(triggerPort), args, mockHost, ct);
            return new SandboxResult(
                true,
                runResult.Logs,
                runResult.EmittedSynapses,
                runResult.SavedResources,
                runResult.Counters);
        }
        catch (Exception ex)
        {
            return new SandboxResult(
                false,
                Array.Empty<string>(),
                Array.Empty<EmittedSynapse>(),
                new Dictionary<string, string>(),
                new Dictionary<string, long>(),
                $"Execution error: {ex.Message}");
        }
    }
}
