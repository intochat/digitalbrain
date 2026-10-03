using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Edge.V1;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Client;

public sealed class DigitalBrainConnection : IDigitalBrain
{
    private readonly IConfigurationRoot _configuration;
    private readonly CancellationTokenSource _stopping = new();
    private readonly PosixSignalRegistration _termination;
    private bool _disposed;
    private readonly HttpClient _http;
    private readonly ScriptEdgeClient _edge;

    internal DigitalBrainConnection(IConfigurationRoot configuration, HttpClient http)
    {
        _configuration = configuration;
        _termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        { context.Cancel = true; _stopping.Cancel(); });
        Console.CancelKeyPress += Cancel;
        _http = http;
        _edge = new ScriptEdgeClient(http);
    }

    public CancellationToken Stopping => _stopping.Token;

    private void Cancel(object? sender, ConsoleCancelEventArgs args) { args.Cancel = true; _stopping.Cancel(); }

    // Environment variables spell configuration sections with "__", so "Account__twitter" arrives as "Account:twitter".
    public string? Setting(string name) => Configuration["CSharpFile:Settings:" + name.Replace("__", ":", StringComparison.Ordinal)];

    // A run started by an armed file's trigger carries that signal; other runs have none.
    public T Trigger<T>() where T : Signal
        => Configuration["CSharpFile:Trigger"] is { Length: > 0 } json
            ? JsonSerializer.Deserialize<T>(json, ScriptEdgeProtocol.Json) ?? throw new InvalidOperationException("The trigger signal is empty.")
            : throw new InvalidOperationException("This run was not started by a trigger.");

    public T Get<T>(string id) where T : class, IGrainWithStringKey
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return NeuronProxy.Create<T>(_edge, id);
    }

    public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
        => source is NeuronProxy neuron
            ? EdgeSignalSubscription<T>.OpenAsync(_edge, neuron, cancellationToken)
            : throw new ArgumentException("Subscribe to a neuron obtained from this connection's Get.", nameof(source));

    public async IAsyncEnumerable<T> On<T>(INeuron source, [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : Signal
    {
        await using var subscription = await SubscribeAsync<T>(source, cancellationToken).ConfigureAwait(false);
        await foreach (var signal in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return signal;
        }
    }

    private IConfiguration Configuration => _configuration;

    public ValueTask DisposeAsync()
    {
        if (_disposed) { return ValueTask.CompletedTask; }
        _disposed = true;
        Console.CancelKeyPress -= Cancel;
        _termination.Dispose();
        _stopping.Cancel();
        _http.Dispose();
        (_configuration as IDisposable)?.Dispose();
        _stopping.Dispose();
        return ValueTask.CompletedTask;
    }
}
