using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.AI.PersonaPlex;

public sealed class PersonaPlexSessionFactory : IPersonaPlexSessionFactory, IHostedService, IAsyncDisposable
{
    private readonly bool _enabled;
    private readonly Func<PersonaPlexOptions, IPersonaPlexModelSet> _loadModelSet;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly ILogger<PersonaPlexSessionFactory> _logger;
    private readonly PersonaPlexOptions _options;
    private readonly HashSet<TrackedPersonaPlexSession> _sessions = [];
    private readonly Lock _sessionsLock = new();
    private readonly Lock _stateLock = new();
    private bool _acceptingSessions;
    private bool _modelConfigurationValidated;
    private FactoryLifecycleState _lifecycleState = FactoryLifecycleState.Active;
    private IPersonaPlexModelSet? _modelSet;
    private PersonaPlexReadiness _readiness;

    public PersonaPlexSessionFactory(
        IOptions<PersonaPlexOptions> options,
        ILogger<PersonaPlexSessionFactory> logger)
        : this(options, logger, static configuredOptions => PersonaPlexModelSet.Load(configuredOptions))
    {
    }

    internal PersonaPlexSessionFactory(
        IOptions<PersonaPlexOptions> options,
        ILogger<PersonaPlexSessionFactory> logger,
        Func<PersonaPlexOptions, IPersonaPlexModelSet> loadModelSet)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(loadModelSet);

        _options = options.Value;
        _enabled = _options.Enabled;
        _logger = logger;
        _loadModelSet = loadModelSet;
        _readiness = new PersonaPlexReadiness(
            _enabled ? PersonaPlexReadinessState.Loading : PersonaPlexReadinessState.Disabled,
            _enabled ? "PersonaPlex runtime is loading." : "PersonaPlex is disabled.",
            false);
    }

    public PersonaPlexReadiness Readiness => Volatile.Read(ref _readiness);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            ThrowIfStartupIsTerminal();
        }

        if (!_enabled)
        {
            SetReadiness(PersonaPlexReadinessState.Disabled, "PersonaPlex is disabled.", false);
            return;
        }

        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishStartupCancellationIfActive();
            throw;
        }

        try
        {
            lock (_stateLock)
            {
                ThrowIfStartupIsTerminal();
            }

            if (_modelSet is not null)
            {
                return;
            }

            lock (_stateLock)
            {
                ThrowIfStartupIsTerminal();
                SetReadiness(
                    PersonaPlexReadinessState.Loading,
                    "PersonaPlex runtime is loading.",
                    _modelConfigurationValidated);
            }

            IPersonaPlexModelSet? loadingModelSet = null;
            try
            {
                _options.Validate();
                loadingModelSet = _loadModelSet(_options);
                lock (_stateLock)
                {
                    _modelConfigurationValidated = true;
                }

                await loadingModelSet.WarmUpAsync(cancellationToken).ConfigureAwait(false);

                lock (_stateLock)
                {
                    if (_lifecycleState == FactoryLifecycleState.Active)
                    {
                        _modelSet = loadingModelSet;
                        loadingModelSet = null;
                        _acceptingSessions = true;
                        SetReadiness(
                            PersonaPlexReadinessState.Ready,
                            "PersonaPlex CUDA runtime is ready.",
                            true);
                    }
                }

                loadingModelSet?.Dispose();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                loadingModelSet?.Dispose();
                PublishStartupCancellationIfActive();
                throw;
            }
            catch (Exception exception)
            {
                loadingModelSet?.Dispose();
                lock (_stateLock)
                {
                    if (_lifecycleState == FactoryLifecycleState.Active)
                    {
                        _acceptingSessions = false;
                        var message = _modelConfigurationValidated
                            ? "PersonaPlex CUDA runtime warm-up failed."
                            : "PersonaPlex model set failed validation or CUDA initialization.";
                        SetReadiness(
                            PersonaPlexReadinessState.Failed,
                            message,
                            _modelConfigurationValidated);
                    }
                }

                _logger.LogError(
                    "PersonaPlex startup failed with error type {ErrorType}; model paths and payloads are omitted.",
                    exception.GetType().Name);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
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

            if (_lifecycleState == FactoryLifecycleState.Active)
            {
                _lifecycleState = FactoryLifecycleState.Stopping;
            }

            _acceptingSessions = false;
            if (_enabled)
            {
                SetReadiness(
                    PersonaPlexReadinessState.Failed,
                    "PersonaPlex runtime is stopping.",
                    _modelConfigurationValidated);
            }
        }

        // Stopping is terminal; caller cancellation cannot abandon owned sessions or model resources.
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await DrainSessionsAsync().ConfigureAwait(false);
            var modelSet = Interlocked.Exchange(ref _modelSet, null);
            modelSet?.Dispose();

            lock (_stateLock)
            {
                if (_lifecycleState == FactoryLifecycleState.Stopping)
                {
                    _lifecycleState = FactoryLifecycleState.Stopped;
                    if (_enabled)
                    {
                        SetReadiness(
                            PersonaPlexReadinessState.Failed,
                            "PersonaPlex runtime is stopped.",
                            _modelConfigurationValidated);
                    }
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
                || !_acceptingSessions)
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
                var modelSet = _modelSet;
                if (_lifecycleState is FactoryLifecycleState.Stopping or FactoryLifecycleState.Stopped
                    || !_acceptingSessions
                    || Readiness.State != PersonaPlexReadinessState.Ready
                    || modelSet is null)
                {
                    throw new InvalidOperationException(Readiness.Message);
                }

                lock (_sessionsLock)
                {
                    if (_sessions.Count >= _options.MaxSessions)
                    {
                        throw new InvalidOperationException("PersonaPlex session limit has been reached.");
                    }

                    var trackedSession = new TrackedPersonaPlexSession(
                        modelSet.CreateSession(),
                        RemoveSession);
                    _sessions.Add(trackedSession);
                    return trackedSession;
                }
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
            if (_enabled)
            {
                SetReadiness(
                    PersonaPlexReadinessState.Failed,
                    "PersonaPlex runtime is disposing.",
                    _modelConfigurationValidated);
            }
        }

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                if (_lifecycleState == FactoryLifecycleState.Disposed)
                {
                    return;
                }
            }

            await DrainSessionsAsync().ConfigureAwait(false);
            var modelSet = Interlocked.Exchange(ref _modelSet, null);
            modelSet?.Dispose();

            lock (_stateLock)
            {
                _lifecycleState = FactoryLifecycleState.Disposed;
                _acceptingSessions = false;
                if (_enabled)
                {
                    SetReadiness(
                        PersonaPlexReadinessState.Failed,
                        "PersonaPlex runtime is disposed.",
                        _modelConfigurationValidated);
                }
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void PublishStartupCancellationIfActive()
    {
        lock (_stateLock)
        {
            if (_lifecycleState != FactoryLifecycleState.Active)
            {
                return;
            }

            _acceptingSessions = false;
            SetReadiness(
                PersonaPlexReadinessState.Failed,
                "PersonaPlex startup was canceled.",
                _modelConfigurationValidated);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(
            _lifecycleState is FactoryLifecycleState.Disposing or FactoryLifecycleState.Disposed,
            this);

    private void ThrowIfStartupIsTerminal()
    {
        ThrowIfDisposed();
        if (_lifecycleState is FactoryLifecycleState.Stopping or FactoryLifecycleState.Stopped)
        {
            throw new InvalidOperationException("PersonaPlex runtime has been stopped and cannot be restarted.");
        }
    }

    private async ValueTask DrainSessionsAsync()
    {
        TrackedPersonaPlexSession[] sessions;
        lock (_sessionsLock)
        {
            sessions = [.. _sessions];
        }

        foreach (var session in sessions)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void RemoveSession(TrackedPersonaPlexSession session)
    {
        lock (_sessionsLock)
        {
            _sessions.Remove(session);
        }
    }

    private void SetReadiness(PersonaPlexReadinessState state, string message, bool isModelConfigurationValid)
    {
        Volatile.Write(ref _readiness, new PersonaPlexReadiness(state, message, isModelConfigurationValid));
        _logger.LogInformation("PersonaPlex readiness changed to {ReadinessState}.", state);
    }

    private sealed class TrackedPersonaPlexSession(
        IPersonaPlexSession inner,
        Action<TrackedPersonaPlexSession> onDisposed) : IPersonaPlexSession
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

    private enum FactoryLifecycleState
    {
        Active,
        Stopping,
        Stopped,
        Disposing,
        Disposed,
    }
}
