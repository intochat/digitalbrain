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
    private bool _acceptingSessions;
    private volatile bool _disposed;
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
        ObjectDisposedException.ThrowIf(_disposed, this);

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
            Volatile.Write(ref _acceptingSessions, false);
            SetReadiness(
                PersonaPlexReadinessState.Failed,
                "PersonaPlex startup was canceled.",
                Readiness.IsModelConfigurationValid);
            throw;
        }

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_modelSet is not null)
            {
                return;
            }

            SetReadiness(PersonaPlexReadinessState.Loading, "PersonaPlex runtime is loading.", false);
            var modelConfigurationValidated = false;
            IPersonaPlexModelSet? loadingModelSet = null;
            try
            {
                _options.Validate();
                loadingModelSet = _loadModelSet(_options);
                modelConfigurationValidated = true;
                await loadingModelSet.WarmUpAsync(cancellationToken).ConfigureAwait(false);
                _modelSet = loadingModelSet;
                loadingModelSet = null;
                Volatile.Write(ref _acceptingSessions, true);
                SetReadiness(PersonaPlexReadinessState.Ready, "PersonaPlex CUDA runtime is ready.", true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                loadingModelSet?.Dispose();
                Volatile.Write(ref _acceptingSessions, false);
                SetReadiness(
                    PersonaPlexReadinessState.Failed,
                    "PersonaPlex startup was canceled.",
                    modelConfigurationValidated);
                throw;
            }
            catch (Exception exception)
            {
                loadingModelSet?.Dispose();
                Volatile.Write(ref _acceptingSessions, false);
                var message = modelConfigurationValidated
                    ? "PersonaPlex CUDA runtime warm-up failed."
                    : "PersonaPlex model set failed validation or CUDA initialization.";
                SetReadiness(
                    PersonaPlexReadinessState.Failed,
                    message,
                    modelConfigurationValidated);
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
        Volatile.Write(ref _acceptingSessions, false);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var isModelConfigurationValid = Readiness.IsModelConfigurationValid;
            if (_enabled)
            {
                SetReadiness(
                    PersonaPlexReadinessState.Failed,
                    "PersonaPlex runtime is stopping.",
                    isModelConfigurationValid);
            }

            await DrainSessionsAsync().ConfigureAwait(false);
            var modelSet = Interlocked.Exchange(ref _modelSet, null);
            modelSet?.Dispose();

            if (_enabled)
            {
                SetReadiness(
                    PersonaPlexReadinessState.Failed,
                    "PersonaPlex runtime is stopped.",
                    isModelConfigurationValid);
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(request.ConnectionId))
        {
            throw new ArgumentException("A PersonaPlex connection ID is required.", nameof(request));
        }

        if (!Volatile.Read(ref _acceptingSessions))
        {
            throw new InvalidOperationException(Readiness.Message);
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var modelSet = _modelSet;
            if (!Volatile.Read(ref _acceptingSessions)
                || Readiness.State != PersonaPlexReadinessState.Ready
                || modelSet is null)
            {
                throw new InvalidOperationException(Readiness.Message);
            }

            var trackedSession = new TrackedPersonaPlexSession(
                modelSet.CreateSession(),
                RemoveSession);
            lock (_sessionsLock)
            {
                _sessions.Add(trackedSession);
            }

            return trackedSession;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        Volatile.Write(ref _acceptingSessions, false);
        if (_enabled)
        {
            SetReadiness(
                PersonaPlexReadinessState.Failed,
                "PersonaPlex runtime is disposing.",
                Readiness.IsModelConfigurationValid);
        }

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Volatile.Write(ref _acceptingSessions, false);
            await DrainSessionsAsync().ConfigureAwait(false);
            var modelSet = Interlocked.Exchange(ref _modelSet, null);
            modelSet?.Dispose();

            if (_enabled)
            {
                SetReadiness(
                    PersonaPlexReadinessState.Failed,
                    "PersonaPlex runtime is disposed.",
                    Readiness.IsModelConfigurationValid);
            }
        }
        finally
        {
            _lifecycleGate.Release();
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
}
