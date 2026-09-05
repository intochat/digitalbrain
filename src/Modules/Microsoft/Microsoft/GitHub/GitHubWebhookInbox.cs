using System.Diagnostics;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("github.webhook-receipt")]
internal sealed record GitHubWebhookReceipt(
    [property: Id(0)] string DeliveryId,
    [property: Id(1)] string Digest,
    [property: Id(2)] string BindingRevision,
    [property: Id(3)] int? PullRequestNumber,
    [property: Id(4)] bool Revoke,
    [property: Id(5)] DateTimeOffset AcceptedAt,
    [property: Id(6)] bool Completed = false,
    [property: Id(7)] DateTimeOffset? NextAttemptAt = null,
    [property: Id(8)] int Attempts = 0,
    [property: Id(9)] string? TraceParent = null,
    [property: Id(10)] string? TraceState = null);

internal enum GitHubReceiptAcceptance { Accepted, Duplicate, Conflict, Unavailable }

[GenerateSerializer, Alias("github.webhook-state")]
internal sealed record GitHubWebhookState
{
    [Id(0)] public List<GitHubWebhookReceipt> Receipts { get; init; } = [];
    [Id(1)] public string? RevokedRevision { get; init; }
}

[Alias("github.webhook-inbox")]
internal interface IGitHubWebhookInbox : IGrainWithStringKey
{
    Task<GitHubReceiptAcceptance> AcceptAsync(GitHubWebhookReceipt receipt);
    Task<GitHubWebhookReceipt[]> ReadPendingAsync(bool includeDeferred = false);
    Task CompleteAsync(string deliveryId, string digest);
    Task RetryAsync(string deliveryId, string digest);
    Task<bool> IsRevokedAsync(string bindingRevision);
}

// This short receipt grain is deliberately separate from every model/repository turn.
[GrainType("github-webhook-inbox")]
internal sealed class GitHubWebhookInbox(
    GitHubRepositoryBindings bindings,
    [PersistentState("receipts", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<GitHubWebhookState> state)
    : Grain, IGitHubWebhookInbox
{
    internal const int Capacity = 4096;
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public async Task<GitHubReceiptAcceptance> AcceptAsync(GitHubWebhookReceipt receipt)
    {
        var binding = RequireBinding();
        receipt = GitHubTelemetry.SanitizeContext(receipt);
        using var activity = GitHubTelemetry.StartReceipt("github.webhook.persist", ActivityKind.Internal, binding, receipt);
        if (receipt.BindingRevision != binding.Revision || receipt.DeliveryId.Length is < 1 or > 100
            || receipt.Digest.Length != 64 || receipt.PullRequestNumber is <= 0)
        {
            return GitHubReceiptAcceptance.Conflict;
        }
        var previous = state.State;
        var existing = previous.Receipts.FirstOrDefault(item => item.DeliveryId == receipt.DeliveryId);
        if (existing is not null)
        {
            return existing.Digest == receipt.Digest && existing.BindingRevision == receipt.BindingRevision
                ? GitHubReceiptAcceptance.Duplicate : GitHubReceiptAcceptance.Conflict;
        }
        if (previous.RevokedRevision == binding.Revision && !receipt.Revoke)
        {
            return GitHubReceiptAcceptance.Unavailable;
        }
        var cutoff = DateTimeOffset.UtcNow - Retention;
        var retained = previous.Receipts.Where(item => !item.Completed || item.AcceptedAt >= cutoff).ToList();
        if (retained.Count >= Capacity)
        {
            return GitHubReceiptAcceptance.Unavailable;
        }
        retained.Add(receipt with { Completed = false });
        await SaveAsync(previous with
        {
            Receipts = retained,
            RevokedRevision = receipt.Revoke ? binding.Revision : previous.RevokedRevision,
        });
        activity?.SetTag("github.webhook.persisted", true);
        if (receipt.Revoke)
        {
            binding.Revoke();
        }
        return GitHubReceiptAcceptance.Accepted;
    }

    public Task<GitHubWebhookReceipt[]> ReadPendingAsync(bool includeDeferred = false)
    {
        _ = RequireBinding();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(state.State.Receipts.Where(item => !item.Completed && (includeDeferred || item.NextAttemptAt is null || item.NextAttemptAt <= now))
            .OrderBy(static item => item.NextAttemptAt ?? item.AcceptedAt).Take(32).ToArray());
    }

    public async Task CompleteAsync(string deliveryId, string digest)
    {
        _ = RequireBinding();
        var previous = state.State;
        var index = previous.Receipts.FindIndex(item => item.DeliveryId == deliveryId && item.Digest == digest);
        if (index < 0 || previous.Receipts[index].Completed)
        {
            return;
        }
        var items = previous.Receipts.ToList();
        items[index] = items[index] with { Completed = true };
        await SaveAsync(previous with { Receipts = items });
    }

    public Task<bool> IsRevokedAsync(string bindingRevision)
    {
        _ = RequireBinding();
        return Task.FromResult(state.State.RevokedRevision == bindingRevision);
    }

    public async Task RetryAsync(string deliveryId, string digest)
    {
        _ = RequireBinding();
        var previous = state.State;
        var index = previous.Receipts.FindIndex(item => item.DeliveryId == deliveryId && item.Digest == digest);
        if (index < 0 || previous.Receipts[index].Completed)
        {
            return;
        }
        var items = previous.Receipts.ToList();
        var attempts = Math.Min(16, items[index].Attempts + 1);
        items[index] = items[index] with
        {
            Attempts = attempts,
            NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, attempts))),
        };
        await SaveAsync(previous with { Receipts = items });
    }

    private GitHubRepositoryBinding RequireBinding()
    {
        var binding = bindings.Find(this.GetPrimaryKeyString())
            ?? throw new InvalidOperationException("The GitHub receipt binding is not configured.");
        if (VerifiedActor.Current?.PrincipalId != binding.Principal)
        {
            throw new UnauthorizedAccessException("The GitHub receipt principal does not match its binding.");
        }
        return binding;
    }

    private async Task SaveAsync(GitHubWebhookState next)
    {
        var previous = state.State;
        state.State = next;
        try
        {
            await state.WriteStateAsync();
        }
        catch
        {
            state.State = previous;
            throw;
        }
    }
}
