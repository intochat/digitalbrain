using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.AI.PersonaPlex;

internal sealed class RemotePersonaPlexSessionFactory : IPersonaPlexSessionFactory, IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan ReadinessPollInterval = TimeSpan.FromSeconds(1);

    private readonly PersonaPlexOptions _options;
    private readonly ILogger<RemotePersonaPlexSessionFactory> _logger;
    private readonly HttpClient _httpClient;
    private readonly Func<ClientWebSocket> _webSocketFactory;
    private readonly object _stateLock = new();
    private readonly object _sessionsLock = new();
    private readonly HashSet<TrackedRemoteSession> _sessions = [];
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private readonly bool _enabled;
    private bool _acceptingSessions;
    private FactoryLifecycleState _lifecycleState = FactoryLifecycleState.Active;
    private PersonaPlexReadiness _readiness;
    private CancellationTokenSource? _readinessLoop;

    public RemotePersonaPlexSessionFactory(
        IOptions<PersonaPlexOptions> options,
        ILogger<RemotePersonaPlexSessionFactory> logger)
        : this(options, logger, new HttpClient(), static () => new ClientWebSocket())
    {
    }

    internal RemotePersonaPlexSessionFactory(
        IOptions<PersonaPlexOptions> options,
        ILogger<RemotePersonaPlexSessionFactory> logger,
        HttpClient httpClient,
        Func<ClientWebSocket> webSocketFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(webSocketFactory);

        _options = options.Value;
        _enabled = _options.Enabled;
        _logger = logger;
        _httpClient = httpClient;
        _webSocketFactory = webSocketFactory;
        _readiness = new PersonaPlexReadiness(
            _enabled ? PersonaPlexReadinessState.Loading : PersonaPlexReadinessState.Disabled,
            _enabled ? "PersonaPlex remote runtime is loading." : "PersonaPlex is disabled.",
            false);
    }

    public PersonaPlexReadiness Readiness => Volatile.Read(ref _readiness);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            _options.Validate();
            SetReadiness(PersonaPlexReadinessState.Disabled, "PersonaPlex is disabled.", false);
            return;
        }

        try
        {
            _options.Validate();
        }
        catch (Exception exception)
        {
            SetReadiness(
                PersonaPlexReadinessState.Failed,
                "PersonaPlex remote runtime configuration is invalid.",
                false);
            _logger.LogError(
                "PersonaPlex remote startup failed with error type {ErrorType}; secrets are omitted.",
                exception.GetType().Name);
            return;
        }

        _readinessLoop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = PollReadinessAsync(_readinessLoop.Token);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (_lifecycleState is FactoryLifecycleState.Stopped
                or FactoryLifecycleState.Disposing
                or FactoryLifecycleState.Disposed)
            {
                return;
            }

            _lifecycleState = FactoryLifecycleState.Stopping;
            _acceptingSessions = false;
            if (_enabled)
            {
                SetReadiness(
                    PersonaPlexReadinessState.Failed,
                    "PersonaPlex remote runtime is stopping.",
                    true);
            }
        }

        _readinessLoop?.Cancel();
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await DrainSessionsAsync().ConfigureAwait(false);
            lock (_stateLock)
            {
                if (_lifecycleState == FactoryLifecycleState.Stopping)
                {
                    _lifecycleState = FactoryLifecycleState.Stopped;
                }
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask<IPersonaPlexSession> CreateAsync(
        PersonaPlexSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.ConnectionId))
        {
            throw new ArgumentException("A PersonaPlex connection ID is required.", nameof(request));
        }

        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (_lifecycleState is FactoryLifecycleState.Stopping or FactoryLifecycleState.Stopped
                || !_acceptingSessions
                || Readiness.State != PersonaPlexReadinessState.Ready)
            {
                throw new InvalidOperationException(Readiness.Message);
            }
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                ThrowIfDisposed();
                if (_lifecycleState is FactoryLifecycleState.Stopping or FactoryLifecycleState.Stopped
                    || !_acceptingSessions
                    || Readiness.State != PersonaPlexReadinessState.Ready)
                {
                    throw new InvalidOperationException(Readiness.Message);
                }
            }

            ClientWebSocket? socket = null;
            try
            {
                socket = _webSocketFactory();
                socket.Options.SetRequestHeader("Authorization", $"Bearer {_options.AdapterToken}");
                await socket.ConnectAsync(BuildStreamUri(), cancellationToken).ConfigureAwait(false);

                lock (_sessionsLock)
                {
                    if (_sessions.Count >= _options.MaxSessions)
                    {
                        throw new InvalidOperationException("PersonaPlex session limit has been reached.");
                    }

                    var tracked = new TrackedRemoteSession(new RemotePersonaPlexSession(socket), RemoveSession);
                    socket = null;
                    _sessions.Add(tracked);
                    return tracked;
                }
            }
            catch
            {
                socket?.Dispose();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_lifecycleState == FactoryLifecycleState.Disposed)
            {
                return;
            }

            _lifecycleState = FactoryLifecycleState.Disposing;
            _acceptingSessions = false;
        }

        _readinessLoop?.Cancel();
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DrainSessionsAsync().ConfigureAwait(false);
            _httpClient.Dispose();
            _readinessLoop?.Dispose();
            lock (_stateLock)
            {
                _lifecycleState = FactoryLifecycleState.Disposed;
            }
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private async Task PollReadinessAsync(CancellationToken cancellationToken)
    {
        var readyz = BuildReadyzUri();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await _httpClient
                    .GetAsync(readyz, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                var payload = await response.Content
                    .ReadFromJsonAsync<AdapterReadinessResponse>(cancellationToken)
                    .ConfigureAwait(false);
                var state = payload?.State?.Trim().ToLowerInvariant();

                if (response.IsSuccessStatusCode && state == "ready")
                {
                    MarkReady(payload?.Message);
                    return;
                }

                if (state == "failed")
                {
                    MarkFailed(payload?.Message ?? "PersonaPlex remote runtime failed.");
                    return;
                }

                SetReadiness(
                    PersonaPlexReadinessState.Loading,
                    payload?.Message ?? "PersonaPlex remote runtime is loading.",
                    true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "PersonaPlex readiness poll failed with error type {ErrorType}.",
                    exception.GetType().Name);
                SetReadiness(
                    PersonaPlexReadinessState.Loading,
                    "PersonaPlex remote runtime is loading.",
                    true);
            }

            try
            {
                await Task.Delay(ReadinessPollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private void MarkReady(string? message)
    {
        lock (_stateLock)
        {
            if (_lifecycleState != FactoryLifecycleState.Active)
            {
                return;
            }

            _acceptingSessions = true;
            SetReadiness(
                PersonaPlexReadinessState.Ready,
                string.IsNullOrWhiteSpace(message)
                    ? "PersonaPlex remote runtime is ready."
                    : message,
                true);
        }
    }

    private void MarkFailed(string message)
    {
        lock (_stateLock)
        {
            if (_lifecycleState != FactoryLifecycleState.Active)
            {
                return;
            }

            _acceptingSessions = false;
            SetReadiness(PersonaPlexReadinessState.Failed, message, true);
        }
    }

    private Uri BuildReadyzUri()
    {
        var endpoint = _options.RuntimeEndpoint.TrimEnd('/');
        return new Uri($"{endpoint}/readyz", UriKind.Absolute);
    }

    private Uri BuildStreamUri()
    {
        var endpoint = new Uri(_options.RuntimeEndpoint, UriKind.Absolute);
        var builder = new UriBuilder(endpoint)
        {
            Scheme = endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = "/stream",
            Query = string.Empty,
        };
        return builder.Uri;
    }

    private async ValueTask DrainSessionsAsync()
    {
        TrackedRemoteSession[] sessions;
        lock (_sessionsLock)
        {
            sessions = [.. _sessions];
        }

        foreach (var session in sessions)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void RemoveSession(TrackedRemoteSession session)
    {
        lock (_sessionsLock)
        {
            _sessions.Remove(session);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(
            _lifecycleState is FactoryLifecycleState.Disposing or FactoryLifecycleState.Disposed,
            this);

    private void SetReadiness(PersonaPlexReadinessState state, string message, bool isModelConfigurationValid)
    {
        Volatile.Write(ref _readiness, new PersonaPlexReadiness(state, message, isModelConfigurationValid));
        _logger.LogInformation("PersonaPlex readiness changed to {ReadinessState}.", state);
    }

    private sealed class TrackedRemoteSession(
        IPersonaPlexSession inner,
        Action<TrackedRemoteSession> onDisposed) : IPersonaPlexSession
    {
        private readonly TaskCompletionSource _disposeCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeStarted;

        public ValueTask<PersonaPlexAudioFrame> ProcessAsync(
            PersonaPlexAudioFrame frame,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
            return inner.ProcessAsync(frame, cancellationToken);
        }

        public ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
            return inner.ResetAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 0)
            {
                _ = DisposeCoreAsync();
            }

            return new ValueTask(_disposeCompletion.Task);
        }

        private async Task DisposeCoreAsync()
        {
            try
            {
                await inner.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _disposeCompletion.TrySetException(exception);
                return;
            }
            finally
            {
                onDisposed(this);
            }

            _disposeCompletion.TrySetResult();
        }
    }

    private sealed class AdapterReadinessResponse
    {
        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private enum FactoryLifecycleState
    {
        Active,
        Stopping,
        Stopped,
        Disposing,
        Disposed,
    }
}
