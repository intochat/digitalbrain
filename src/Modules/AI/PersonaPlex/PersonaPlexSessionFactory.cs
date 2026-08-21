using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.AI.PersonaPlex;

public sealed class PersonaPlexSessionFactory : IPersonaPlexSessionFactory, IHostedService, IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly ILogger<PersonaPlexSessionFactory> _logger;
    private readonly PersonaPlexOptions _options;
    private PersonaPlexModelSet? _modelSet;
    private PersonaPlexReadiness _readiness;
    private bool _disposed;

    public PersonaPlexSessionFactory(
        IOptions<PersonaPlexOptions> options,
        ILogger<PersonaPlexSessionFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _readiness = new PersonaPlexReadiness(
            _options.Enabled ? PersonaPlexReadinessState.Loading : PersonaPlexReadinessState.Disabled,
            _options.Enabled ? "PersonaPlex runtime is loading." : "PersonaPlex is disabled.",
            false);
    }

    public PersonaPlexReadiness Readiness => Volatile.Read(ref _readiness);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_options.Enabled)
        {
            SetReadiness(PersonaPlexReadinessState.Disabled, "PersonaPlex is disabled.", false);
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_modelSet is not null)
            {
                return;
            }

            SetReadiness(PersonaPlexReadinessState.Loading, "PersonaPlex runtime is loading.", false);
            var configurationValidated = false;
            PersonaPlexModelSet? loadingModelSet = null;
            try
            {
                _options.Validate();
                configurationValidated = true;
                loadingModelSet = PersonaPlexModelSet.Load(_options);
                await loadingModelSet.WarmUpAsync(cancellationToken).ConfigureAwait(false);
                _modelSet = loadingModelSet;
                loadingModelSet = null;
                SetReadiness(PersonaPlexReadinessState.Ready, "PersonaPlex CUDA runtime is ready.", true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                loadingModelSet?.Dispose();
                throw;
            }
            catch (Exception exception)
            {
                loadingModelSet?.Dispose();
                var manifestCompatible = exception is not PersonaPlexModelManifestException;
                var message = configurationValidated && manifestCompatible
                    ? "PersonaPlex CUDA runtime failed to load the configured model set."
                    : "PersonaPlex model-manifest incompatibility or incomplete model configuration.";
                SetReadiness(
                    PersonaPlexReadinessState.Failed,
                    message,
                    configurationValidated && manifestCompatible);
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
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var modelSet = Interlocked.Exchange(ref _modelSet, null);
            modelSet?.Dispose();
            if (_options.Enabled)
            {
                SetReadiness(PersonaPlexReadinessState.Failed, "PersonaPlex runtime is stopped.", false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public ValueTask<IPersonaPlexSession> CreateAsync(
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

        var modelSet = Volatile.Read(ref _modelSet);
        if (Readiness.State != PersonaPlexReadinessState.Ready || modelSet is null)
        {
            return ValueTask.FromException<IPersonaPlexSession>(
                new InvalidOperationException(Readiness.Message));
        }

        return ValueTask.FromResult<IPersonaPlexSession>(new PersonaPlexSession(modelSet));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var modelSet = Interlocked.Exchange(ref _modelSet, null);
            modelSet?.Dispose();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void SetReadiness(PersonaPlexReadinessState state, string message, bool isModelConfigurationValid)
    {
        Volatile.Write(ref _readiness, new PersonaPlexReadiness(state, message, isModelConfigurationValid));
        _logger.LogInformation("PersonaPlex readiness changed to {ReadinessState}.", state);
    }
}
