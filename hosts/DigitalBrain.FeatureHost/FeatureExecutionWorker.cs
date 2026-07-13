using System.Text.Json;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.Hosting;
using SdkInput = DigitalBrain.Features.Sdk.FeatureInput;

namespace DigitalBrain.FeatureHost;

public sealed record FeatureWorkItem(
    FeatureInstallationId InstallationId,
    IFeatureInstallationGrain Installation);

public interface IFeatureWorkSource
{
    ValueTask<FeatureWorkItem> TakeAsync(CancellationToken cancellationToken = default);
}

public interface IFeatureRunContextFactory
{
    ValueTask<IFeatureRunContext> CreateAsync(
        FeatureWorkItem work,
        FeatureRunClaim claim,
        CancellationToken cancellationToken = default);
}

public interface IFeatureRunContext : IAsyncDisposable
{
    IFeatureContext Context { get; }
    FeatureRunCommit CreateCommit(FeatureLeaseFence fence);
}

public sealed class FeatureExecutionOptions
{
    public static readonly TimeSpan MaximumHandlerDeadline = TimeSpan.FromSeconds(60);

    public FeatureExecutionOptions(
        string hostId,
        TimeSpan handlerDeadline,
        TimeSpan persistenceDeadline,
        TimeSpan retryDelay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        if (hostId.Length > 256 || hostId.Any(char.IsControl) ||
            !string.Equals(hostId, hostId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical host identifier is required.", nameof(hostId));
        if (handlerDeadline <= TimeSpan.Zero || handlerDeadline > MaximumHandlerDeadline)
            throw new ArgumentOutOfRangeException(nameof(handlerDeadline));
        if (persistenceDeadline <= TimeSpan.Zero || persistenceDeadline > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(persistenceDeadline));
        if (retryDelay < TimeSpan.Zero || retryDelay > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        HostId = hostId;
        HandlerDeadline = handlerDeadline;
        PersistenceDeadline = persistenceDeadline;
        RetryDelay = retryDelay;
    }

    public string HostId { get; }
    public TimeSpan HandlerDeadline { get; }
    public TimeSpan PersistenceDeadline { get; }
    public TimeSpan RetryDelay { get; }
}

public sealed class FeatureExecutionWorker : BackgroundService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    private readonly FeatureReleaseManager _releases;
    private readonly IFeatureWorkSource _workSource;
    private readonly IFeatureRunContextFactory _contexts;
    private readonly IFeatureHostRecycle _recycle;
    private readonly FeatureExecutionOptions _options;
    private readonly TimeProvider _timeProvider;
    private int _recycleRequested;
    private int _running;

    public FeatureExecutionWorker(
        FeatureReleaseManager releases,
        IFeatureWorkSource workSource,
        IFeatureRunContextFactory contexts,
        IFeatureHostRecycle recycle,
        FeatureExecutionOptions options,
        TimeProvider timeProvider)
    {
        _releases = releases ?? throw new ArgumentNullException(nameof(releases));
        _workSource = workSource ?? throw new ArgumentNullException(nameof(workSource));
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _recycle = recycle ?? throw new ArgumentNullException(nameof(recycle));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task RunAsync(CancellationToken cancellationToken = default) => RunLoopAsync(cancellationToken);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
            throw new InvalidOperationException("FeatureHost owns exactly one execution loop.");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var work = await _workSource.TakeAsync(stoppingToken);
                FeatureRunClaim? claim;
                try
                {
                    claim = await Task.Run(
                            async () => await work.Installation.ClaimAsync(_options.HostId, LeaseDuration),
                            stoppingToken)
                        .WaitAsync(_options.PersistenceDeadline, stoppingToken);
                }
                catch (TimeoutException)
                {
                    RequestRecycle();
                    return;
                }
                if (claim is not null)
                    await ExecuteClaimAsync(work, claim, stoppingToken);
                if (Volatile.Read(ref _recycleRequested) != 0)
                    return;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch
        {
            RequestRecycle();
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task ExecuteClaimAsync(
        FeatureWorkItem work,
        FeatureRunClaim claim,
        CancellationToken stoppingToken)
    {
        FeatureReleaseLease? lease = null;
        IFeatureRunContext? runContext = null;
        var commitStarted = false;
        var unsafeExecution = false;
        try
        {
            var reserve = _options.PersistenceDeadline + _options.PersistenceDeadline;
            var available = claim.LeaseExpiresAt - _timeProvider.GetUtcNow() - reserve;
            var executionBudget = available < _options.HandlerDeadline
                ? available
                : _options.HandlerDeadline;
            if (executionBudget <= TimeSpan.Zero)
            {
                await FailAsync(work, claim, "feature lease budget insufficient", stoppingToken);
                return;
            }

            var executionEnds = _timeProvider.GetUtcNow() + executionBudget;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(executionBudget);
            try
            {
                var acquireBudget = executionEnds - _timeProvider.GetUtcNow();
                if (acquireBudget <= TimeSpan.Zero)
                    throw new TimeoutException();
                lease = await Task.Run(
                        () => _releases.Acquire(work.InstallationId, claim.Release),
                        stoppingToken)
                    .WaitAsync(acquireBudget, stoppingToken);
                var contextBudget = executionEnds - _timeProvider.GetUtcNow();
                if (contextBudget <= TimeSpan.Zero)
                    throw new TimeoutException();
                runContext = await Task.Run(
                        async () => await _contexts.CreateAsync(work, claim, deadline.Token),
                        stoppingToken)
                    .WaitAsync(contextBudget, stoppingToken);
            }
            catch (TimeoutException)
            {
                unsafeExecution = true;
                RequestRecycle();
                return;
            }

            var input = ToSdkInput(claim.Input);
            var handler = Task.Run(
                async () => await lease.Feature.HandleAsync(input, runContext.Context, deadline.Token),
                stoppingToken);
            try
            {
                var remainingExecution = executionEnds - _timeProvider.GetUtcNow();
                if (remainingExecution <= TimeSpan.Zero)
                    throw new OperationCanceledException(deadline.Token);
                await handler.WaitAsync(remainingExecution, stoppingToken);
            }
            catch (TimeoutException) when (!handler.IsCompleted)
            {
                deadline.Cancel();
                try
                {
                    await handler.WaitAsync(_options.PersistenceDeadline, stoppingToken);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    await FailAsync(work, claim, "feature execution deadline exceeded", stoppingToken);
                    return;
                }
                catch (TimeoutException) when (!handler.IsCompleted)
                {
                    unsafeExecution = true;
                    RequestRecycle();
                    return;
                }
            }
            deadline.Token.ThrowIfCancellationRequested();
            var commit = runContext.CreateCommit(claim.Fence);
            var commitBudget = claim.LeaseExpiresAt - _timeProvider.GetUtcNow();
            if (commitBudget <= TimeSpan.Zero)
                return;
            if (commitBudget > _options.PersistenceDeadline)
                commitBudget = _options.PersistenceDeadline;
            commitStarted = true;
            await work.Installation
                .CommitAsync(commit)
                .WaitAsync(commitBudget, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!commitStarted)
        {
            await FailAsync(work, claim, "feature execution deadline exceeded", stoppingToken);
        }
        catch (FeatureReleaseUnavailableException)
        {
            await FailAsync(work, claim, "feature release unavailable", stoppingToken);
        }
        catch when (commitStarted)
        {
        }
        catch
        {
            await FailAsync(work, claim, "feature execution failed", stoppingToken);
        }
        finally
        {
            if (unsafeExecution)
            {
                lease?.Abandon();
            }
            else
            {
                var contextDisposed = runContext is null ||
                    await TryDisposeAsync(runContext, _options.PersistenceDeadline);
                if (!contextDisposed)
                {
                    lease?.Abandon();
                    RequestRecycle();
                }
                else if (lease is not null && !await lease.TryDisposeAsync(_options.PersistenceDeadline))
                {
                    RequestRecycle();
                }
            }
        }
    }

    private async Task FailAsync(
        FeatureWorkItem work,
        FeatureRunClaim claim,
        string safeFailure,
        CancellationToken stoppingToken)
    {
        try
        {
            await work.Installation.FailAsync(
                    claim.Fence,
                    _timeProvider.GetUtcNow() + _options.RetryDelay,
                    safeFailure)
                .WaitAsync(_options.PersistenceDeadline, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }
    }

    private static SdkInput ToSdkInput(DigitalBrain.Kernel.Contracts.FeatureInput input)
    {
        using var document = JsonDocument.Parse(input.PayloadJson, new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Feature input payloads must be JSON objects.", nameof(input));
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!facts.TryAdd(
                    property.Name,
                    property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()!
                        : property.Value.GetRawText()))
                throw new ArgumentException("Feature input facts must be unique.", nameof(input));
        }

        return new SdkInput(input.InputId, input.Kind, input.OccurredAt, facts);
    }

    private static async Task<bool> TryDisposeAsync(IAsyncDisposable value, TimeSpan deadline)
    {
        try
        {
            await Task.Run(async () => await value.DisposeAsync()).WaitAsync(deadline);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RequestRecycle()
    {
        if (Interlocked.Exchange(ref _recycleRequested, 1) == 0)
            _recycle.RequestRecycle();
    }
}
