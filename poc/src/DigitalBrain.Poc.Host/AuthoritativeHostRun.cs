using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Host;

internal sealed class AuthoritativeHostRun : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Process _process;
    private readonly Task<string> _standardError;
    private readonly string _authorityControlToken;
    private readonly SemaphoreSlim _protocolGate = new(1, 1);
    private int _stopped;
    private int _disposed;

    public AuthoritativeHostRun(
        Process process,
        Task<string> standardError,
        string authorityControlToken,
        Uri projectionBaseUri)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _standardError = standardError ?? throw new ArgumentNullException(nameof(standardError));
        _authorityControlToken = authorityControlToken ?? throw new ArgumentNullException(
            nameof(authorityControlToken));
        ProjectionBaseUri = projectionBaseUri ?? throw new ArgumentNullException(nameof(projectionBaseUri));
        if (!ProjectionBaseUri.IsLoopback || ProjectionBaseUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidDataException("The active chart projection URI must be loopback HTTP.");
        }
    }

    public int ProcessId => _process.Id;

    public Uri ProjectionBaseUri { get; }

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public IngressQuiesceGate Ingress { get; } = new();

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _process.WaitForExitAsync(cancellationToken);

    public async Task ReleaseAuthorityAsync(CancellationToken cancellationToken) =>
        _ = await SendAsync<object>(
            "release-host-authority",
            new AuthorityControlWire(_authorityControlToken),
            cancellationToken);

    public async Task ReacquireAuthorityAsync(CancellationToken cancellationToken) =>
        _ = await SendAsync<object>(
            "reacquire-host-authority",
            new AuthorityControlWire(_authorityControlToken),
            cancellationToken);

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        Ingress.Close();
        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (_process.HasExited)
                {
                }
            }

            if (!_process.HasExited)
            {
                await _process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException) when (_process.HasExited)
        {
        }
    }

    public async Task<T> SendAsync<T>(
        string command,
        object payload,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            throw new HostQuiescingException();
        }

        await _protocolGate.WaitAsync(cancellationToken);
        try
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Active host exited with code {_process.ExitCode}:{Environment.NewLine}{await _standardError}");
            }

            var id = Guid.NewGuid().ToString("N");
            var request = JsonSerializer.Serialize(
                new ScenarioWireRequest(
                    id,
                    command,
                    JsonSerializer.SerializeToElement(payload, JsonOptions)),
                JsonOptions);
            await _process.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken) ??
                throw new EndOfStreamException(
                    $"Active host closed its protocol stream:{Environment.NewLine}{await _standardError}");
            var response = JsonSerializer.Deserialize<ScenarioWireResponse>(line, JsonOptions) ??
                throw new InvalidDataException("The active host returned an empty response.");
            if (!string.Equals(response.Id, id, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The active host response correlation is invalid.");
            }

            if (!response.Success)
            {
                throw response.ErrorType switch
                {
                    nameof(AuthorizationException) => new AuthorizationException(response.ErrorMessage!),
                    nameof(CapabilityDeniedException) => new CapabilityDeniedException(response.ErrorMessage!),
                    _ => new InvalidOperationException(
                        $"Remote {response.ErrorType}: {response.ErrorMessage}"),
                };
            }

            if (typeof(T) == typeof(object))
            {
                return (T)new object();
            }

            return response.Payload.Deserialize<T>(JsonOptions) ??
                throw new InvalidDataException($"The active host returned no '{typeof(T).Name}' payload.");
        }
        finally
        {
            _protocolGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync();
        _process.Dispose();
        _protocolGate.Dispose();
    }

    private sealed record ScenarioWireRequest(string Id, string Command, JsonElement Payload);

    private sealed record AuthorityControlWire(string Token);

    private sealed record ScenarioWireResponse(
        string Id,
        bool Success,
        JsonElement Payload,
        string? ErrorType,
        string? ErrorMessage);
}
