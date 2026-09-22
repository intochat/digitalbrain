using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace DigitalBrain.Coding;

internal sealed class CodeCheckCoordinator(IOptions<CodeExecutionOptions> options, IGrainFactory grains, ILogger<CodeCheckCoordinator> logger) : BackgroundService
{
    private readonly Channel<(string Id, Guid Operation)> _queue = Channel.CreateBounded<(string, Guid)>(32);
    private readonly ConcurrentDictionary<(string, Guid), CancellationTokenSource> _active = new();
    private FileStream? _ownership;
    private CodeDraftStore? _store;
    public CodeDraftStore Store => _store ?? throw new InvalidOperationException("Managed draft execution is not configured.");

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Root is { Length: > 0 } root && !string.IsNullOrWhiteSpace(root))
        {
            Directory.CreateDirectory(root);
            _ownership = new(Path.Combine(root, "execution.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _store = new(Path.Combine(root, "drafts"));
            await Store.InterruptPendingAsync(cancellationToken).ConfigureAwait(false);
        }
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(string id, Guid operation, CancellationToken ct)
    {
        if (!_queue.Writer.TryWrite((id, operation)))
        {
            await Store.UpdateCheckAsync(id, operation, check => check with { Status = CodeCheckStatus.Failed,
                CompletedAt = DateTimeOffset.UtcNow, Diagnostics = [new("queue-full", "error", "The check queue is full; retry with a new operation ID.")] }, ct).ConfigureAwait(false);
        }
    }

    public async Task CancelAsync(string id, Guid operation, CancellationToken ct)
    {
        await Store.UpdateCheckAsync(id, operation, check => check with { Status = CodeCheckStatus.Cancelled, CompletedAt = DateTimeOffset.UtcNow }, ct).ConfigureAwait(false);
        if (_active.TryGetValue((id, operation), out var running))
        {
            try { await running.CancelAsync().ConfigureAwait(false); }
            catch (ObjectDisposedException) { /* The check settled between lookup and cancellation. */ }
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(Consume(stoppingToken), Consume(stoppingToken));

    private async Task Consume(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                if (!_active.TryAdd(job, lifetime)) { continue; }
                try
                {
                    var check = await Store.ReadCheckAsync(job.Id, job.Operation, lifetime.Token).ConfigureAwait(false);
                    if (CodeDraftStore.IsTerminal(check.Status)) { continue; }
                    var input = await Store.ReadInputAsync(job.Id, job.Operation, lifetime.Token).ConfigureAwait(false);
                    var validator = new CodeValidationService(options.Value);
                    var result = await validator.ValidateAsync(input, check, async status =>
                    {
                        var state = await Store.UpdateCheckAsync(job.Id, job.Operation, c => c with { Status = status }, lifetime.Token).ConfigureAwait(false);
                        if (state.Status == CodeCheckStatus.Cancelled) { throw new OperationCanceledException(lifetime.Token); }
                        await Notify(job.Id, job.Operation).ConfigureAwait(false);
                    }, lifetime.Token).ConfigureAwait(false);
                    await Store.UpdateCheckAsync(job.Id, job.Operation, _ => result, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    var message = error.Message.Length > 65536 ? error.Message[..65536] : error.Message;
                    try
                    {
                        await Store.UpdateCheckAsync(job.Id, job.Operation, c => c with
                        {
                            Status = stoppingToken.IsCancellationRequested ? CodeCheckStatus.Interrupted
                                : error is OperationCanceledException ? CodeCheckStatus.Cancelled : CodeCheckStatus.Failed,
                            CompletedAt = DateTimeOffset.UtcNow,
                            Diagnostics = error is CodeValidationException validation ? validation.Diagnostics : [new("validation", "error", message)],
                        }, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception persistence) { logger.LogError(persistence, "Cannot persist check termination for {Draft}", job.Id); }
                }
                finally
                {
                    _active.TryRemove(job, out _);
                    await Notify(job.Id, job.Operation).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task Notify(string id, Guid operation)
    {
        try { await grains.GetGrain<ICodeDraftEvents>(id).Changed(operation).ConfigureAwait(false); }
        catch (Exception error) { logger.LogWarning(error, "Check notification failed for {Draft}; persisted state remains authoritative.", id); }
    }

    public override void Dispose() { base.Dispose(); _ownership?.Dispose(); }
}
