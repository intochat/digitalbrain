using System.Security.Cryptography;
using System.Text;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Orleans.Concurrency;

namespace Brain.Core.Runtime;

[Reentrant]
public sealed class BrainRuntimeGrain(
    IEnumerable<IBrainOperationHandler> handlers,
    IGrainFactory grains) : Grain, IBrainRuntimeGrain
{
    private readonly IReadOnlyDictionary<string, IBrainOperationHandler> _handlers = handlers
        .ToDictionary(handler => handler.Descriptor.Id, StringComparer.Ordinal);

    public Task<IReadOnlyList<BrainModuleDescriptor>> GetModulesAsync()
        => Task.FromResult<IReadOnlyList<BrainModuleDescriptor>>(
            _handlers.Values
                .Select(handler => handler.Descriptor.ModuleId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(module => new BrainModuleDescriptor(
                    module,
                    char.ToUpperInvariant(module[0]) + module[1..],
                    "ready"))
                .ToArray());

    public Task<IReadOnlyList<BrainOperationDescriptor>> GetOperationsAsync()
        => Task.FromResult<IReadOnlyList<BrainOperationDescriptor>>(
            _handlers.Values
                .Select(handler => handler.Descriptor)
                .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
                .ToArray());

    public async Task<BrainActivityReceipt> InvokeAsync(BrainOperationInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (!_handlers.TryGetValue(invocation.OperationId, out var handler))
        {
            throw new KeyNotFoundException($"Operation '{invocation.OperationId}' is not installed.");
        }

        var activityId = ActivityId(invocation);
        var activity = grains.GetGrain<IBrainActivityGrain>(
            $"{invocation.WorkspaceId}/{activityId:n}");
        var receipt = await activity.StartAsync(activityId, invocation);
        var existing = await activity.GetAsync(invocation.WorkspaceId);
        if (existing?.Status == ActivityStatus.Completed)
        {
            return receipt;
        }

        var context = new BrainOperationExecutionContext(activityId, invocation, activity, grains);
        await context.JournalAsync(
            "operation-accepted",
            "core/operation-gateway/workspace",
            BrainJournalDirection.Operation,
            invocation.OperationId,
            "accepted",
            $"Operation {invocation.OperationId} accepted");
        try
        {
            var resultJson = await handler.ExecuteAsync(context, CancellationToken.None);
            await context.JournalAsync(
                "operation-completed",
                "core/operation-gateway/workspace",
                BrainJournalDirection.Operation,
                invocation.OperationId,
                "completed",
                $"Operation {invocation.OperationId} completed");
            await activity.CompleteAsync(invocation.WorkspaceId, resultJson);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await context.JournalAsync(
                "operation-failed",
                "core/operation-gateway/workspace",
                BrainJournalDirection.Operation,
                invocation.OperationId,
                "failed",
                exception.Message);
            await activity.FailAsync(invocation.WorkspaceId, exception.Message);
        }

        return receipt;
    }

    public async Task<BrainChildOperationResult> InvokeWithinActivityAsync(
        Guid activityId,
        BrainOperationInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (!_handlers.TryGetValue(invocation.OperationId, out var handler))
        {
            throw new KeyNotFoundException($"Operation '{invocation.OperationId}' is not installed.");
        }
        var activity = grains.GetGrain<IBrainActivityGrain>(
            $"{invocation.WorkspaceId}/{activityId:n}");
        var existing = await activity.GetAsync(invocation.WorkspaceId)
            ?? throw new KeyNotFoundException($"Activity '{activityId:n}' was not found.");
        if (existing.Status is ActivityStatus.Completed or ActivityStatus.Failed)
        {
            throw new InvalidOperationException("A child Operation cannot run after its parent activity is terminal.");
        }

        var context = new BrainOperationExecutionContext(activityId, invocation, activity, grains);
        var label = $"child:{invocation.OperationId}:{invocation.IdempotencyKey}";
        await context.JournalAsync(
            $"{label}:accepted",
            "core/operation-gateway/workspace",
            BrainJournalDirection.Operation,
            invocation.OperationId,
            "accepted",
            $"Child Operation {invocation.OperationId} accepted");
        try
        {
            var result = await handler.ExecuteAsync(context, CancellationToken.None);
            await context.JournalAsync(
                $"{label}:completed",
                "core/operation-gateway/workspace",
                BrainJournalDirection.Operation,
                invocation.OperationId,
                "completed",
                $"Child Operation {invocation.OperationId} completed");
            return new BrainChildOperationResult(invocation.OperationId, result);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await context.JournalAsync(
                $"{label}:failed",
                "core/operation-gateway/workspace",
                BrainJournalDirection.Operation,
                invocation.OperationId,
                "failed",
                exception.Message);
            throw;
        }
    }

    public Task<BrainActivitySnapshot?> GetActivityAsync(Guid activityId, string workspaceId)
    {
        if (activityId == Guid.Empty)
        {
            throw new ArgumentException("An activity identity is required.", nameof(activityId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        return grains
            .GetGrain<IBrainActivityGrain>($"{workspaceId}/{activityId:n}")
            .GetAsync(workspaceId);
    }

    private static Guid ActivityId(BrainOperationInvocation invocation)
    {
        var material = Encoding.UTF8.GetBytes(
            $"{invocation.WorkspaceId}\0{invocation.PrincipalId}\0{invocation.IdempotencyKey}");
        var hash = SHA256.HashData(material);
        var id = new Guid(hash.AsSpan(0, 16));
        return id == Guid.Empty ? new Guid(hash.AsSpan(16, 16)) : id;
    }
}
