using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("github.repository-dispatcher")]
internal interface IGitHubRepositoryDispatcher : IGrainWithStringKey
{
    Task DispatchAsync(string bindingId, GitHubWebhookReceipt receipt, CancellationToken cancellationToken);
}

[GrainType("github-dispatcher")]
internal sealed class GitHubRepositoryDispatcher(NeuronRuntime runtime, GitHubRepositoryBindings bindings)
    : Neuron(runtime), IGitHubRepositoryDispatcher
{
    public async Task DispatchAsync(string bindingId, GitHubWebhookReceipt receipt, CancellationToken cancellationToken)
    {
        var binding = bindings.Find(bindingId) ?? throw new UnauthorizedAccessException("The GitHub binding is not configured.");
        if (VerifiedActor.Current?.PrincipalId != binding.Principal || Id.Owner != binding.Owner
            || Id.Name != binding.InstanceName || receipt.BindingRevision != binding.Revision)
        {
            throw new UnauthorizedAccessException("The GitHub dispatcher is not bound to this repository and principal.");
        }
        var repository = NeuronId.For<IRepository>(binding.Owner, binding.InstanceName);
        using var activity = GitHubTelemetry.StartReceipt("github.webhook.dispatch", ActivityKind.Consumer, binding, receipt);
        activity?.SetTag("github.webhook.attempt", receipt.Attempts + 1);
        try
        {
            await RequestAsync(repository, new RefreshRepository(binding.Id, receipt.DeliveryId,
                receipt.BindingRevision, receipt.PullRequestNumber, receipt.Revoke), cancellationToken);
            activity?.SetTag("github.webhook.dispatched", true);
        }
        catch (Exception error)
        {
            GitHubTelemetry.Failed(activity, error);
            throw;
        }
    }
}

// Accepted work survives this process. The service only wakes durable stages; no channel is
// treated as a queue, and neither the owner root nor model work participates in HTTP acceptance.
internal sealed class GitHubWebhookDispatcher(
    GitHubRepositoryBindings bindings, IGrainFactory grains, ILogger<GitHubWebhookDispatcher> logger,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    internal static readonly TimeSpan ReconciliationPeriod = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hosted-service registration can precede Orleans startup. Never await a grain
        // during StartAsync: the application must finish starting before recovery calls.
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => ready.TrySetResult());
        await ready.Task.WaitAsync(stoppingToken);
        await Task.WhenAll(bindings.All.Select(binding => DispatchBindingAsync(binding, stoppingToken)));
    }

    private async Task DispatchBindingAsync(GitHubRepositoryBinding binding, CancellationToken stoppingToken)
    {
        var nextReconciliation = DateTimeOffset.MinValue;
        var recovered = false;
        var revokedRepositoryWoken = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var actor = VerifiedActor.Enter(new ActorContext(binding.Principal, "github-dispatch"));
                var inbox = grains.GetGrain<IGitHubWebhookInbox>(binding.Id);
                if (!recovered)
                {
                    if (await inbox.IsRevokedAsync(binding.Revision).WaitAsync(stoppingToken))
                    {
                        binding.Revoke();
                    }
                    binding.CompleteRecovery();
                    recovered = true;
                }
                var dispatcherId = new NeuronId("github-dispatcher", binding.Owner, binding.InstanceName);
                var dispatcher = grains.GetGrain<IGitHubRepositoryDispatcher>(dispatcherId.ToGrainId());
                foreach (var receipt in await inbox.ReadPendingAsync().WaitAsync(stoppingToken))
                {
                    try
                    {
                        if (receipt.BindingRevision == binding.Revision)
                        {
                            await dispatcher.DispatchAsync(binding.Id, receipt, stoppingToken);
                        }
                        await inbox.CompleteAsync(receipt.DeliveryId, receipt.Digest).WaitAsync(stoppingToken);
                    }
                    catch (Exception error) when (error is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                    {
                        // One inaccessible/deleted PR cannot starve other accepted updates.
                        await inbox.RetryAsync(receipt.DeliveryId, receipt.Digest).WaitAsync(stoppingToken);
                        logger.LogWarning("GitHub receipt will retry binding {BindingId} after {FailureType}.", binding.Id, error.GetType().Name);
                    }
                }
                if (!binding.Enabled && !revokedRepositoryWoken)
                {
                    // A crash can follow receipt completion but precede notification flush.
                    // Revoked repositories still need activation to recover their outbox.
                    await dispatcher.DispatchAsync(binding.Id,
                        new GitHubWebhookReceipt($"recover-revoked:{Guid.NewGuid():N}", new string('0', 64), binding.Revision,
                            null, true, DateTimeOffset.UtcNow), stoppingToken);
                    revokedRepositoryWoken = true;
                }
                if (binding.Enabled && DateTimeOffset.UtcNow >= nextReconciliation)
                {
                    var now = DateTimeOffset.UtcNow;
                    nextReconciliation = now + ReconciliationPeriod;
                    await dispatcher.DispatchAsync(binding.Id,
                        new GitHubWebhookReceipt($"reconcile:{Guid.NewGuid():N}", new string('0', 64), binding.Revision, null, false, now), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                // Provider exception bodies may include remote content; keep operational logs bounded and safe.
                logger.LogWarning("GitHub dispatch will retry binding {BindingId} after {FailureType}.", binding.Id, error.GetType().Name);
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
