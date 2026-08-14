using System.Security.Cryptography;
using System.Text;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Policy;
using Brain.Core.Activities;
using Brain.Core.Modules;
using DigitalBrain.ProductHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DigitalBrain.ProductHost.Hosting;

public interface IProductActivityProjectionService
{
    Task<ActivityView> ObserveAsync(
        BrainActivityId activity,
        WorkspaceContext caller,
        CancellationToken cancellationToken);
}

public sealed record DurableProductDelivery(
    string DeliveryKey,
    WorkspaceId Workspace,
    string PayloadReference);

public interface IDurableProductDeliveryService
{
    Task<bool> EnqueueAsync(DurableProductDelivery delivery, CancellationToken cancellationToken);

    Task CompleteAsync(
        string deliveryKey,
        WorkspaceId workspace,
        CancellationToken cancellationToken);

    Task<bool> IsCompletedAsync(
        string deliveryKey,
        WorkspaceId workspace,
        CancellationToken cancellationToken);
}

public abstract class ProductOperationRuntimeRegistration
{
    private protected ProductOperationRuntimeRegistration(OperationDescriptor operation)
    {
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
    }

    public OperationDescriptor Operation { get; }

    internal abstract Type InputType { get; }

    internal abstract Type ResultType { get; }

    internal abstract string Canonicalize(object input);

    internal abstract Task<ActivityResultReference> InvokeAsync(
        object input,
        WorkspaceContext caller,
        CancellationToken cancellationToken);

    public static ProductOperationRuntimeRegistration Create<TInput, TResult>(
        OperationDescriptor operation,
        Func<TInput, string> canonicalize,
        Func<TInput, WorkspaceContext, CancellationToken, Task<ActivityResultReference>> invoke)
        where TInput : class
        where TResult : class
        => new TypedProductOperationRuntimeRegistration<TInput, TResult>(
            operation,
            canonicalize,
            invoke);

    private sealed class TypedProductOperationRuntimeRegistration<TInput, TResult>(
        OperationDescriptor operation,
        Func<TInput, string> canonicalize,
        Func<TInput, WorkspaceContext, CancellationToken, Task<ActivityResultReference>> invoke)
        : ProductOperationRuntimeRegistration(operation)
        where TInput : class
        where TResult : class
    {
        private readonly Func<TInput, string> _canonicalize = canonicalize
            ?? throw new ArgumentNullException(nameof(canonicalize));
        private readonly Func<TInput, WorkspaceContext, CancellationToken, Task<ActivityResultReference>> _invoke = invoke
            ?? throw new ArgumentNullException(nameof(invoke));

        internal override Type InputType => typeof(TInput);

        internal override Type ResultType => typeof(TResult);

        internal override string Canonicalize(object input)
            => _canonicalize((TInput)input);

        internal override Task<ActivityResultReference> InvokeAsync(
            object input,
            WorkspaceContext caller,
            CancellationToken cancellationToken)
            => _invoke((TInput)input, caller, cancellationToken);
    }
}

internal sealed class DurableProductOperationGateway(
    ProductDbContext database,
    IModuleRegistry modules,
    IWorkspacePolicyEvaluator policy,
    IEnumerable<ProductOperationRuntimeRegistration> registrations,
    IProductActivityProjectionService projections,
    TimeProvider timeProvider)
    : IOperationGateway
{
    private readonly IReadOnlyDictionary<OperationId, ProductOperationRuntimeRegistration> _registrations =
        registrations.ToDictionary(static registration => registration.Operation.Id);

    public async Task<OperationAccepted> InvokeAsync<TInput, TResult>(
        OperationDescriptor operation,
        TInput input,
        WorkspaceContext caller,
        IdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
        where TInput : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();
        var installed = modules.GetOperation(operation.Id);
        if (installed != operation
            || !_registrations.TryGetValue(operation.Id, out var registration)
            || registration.Operation != operation
            || registration.InputType != typeof(TInput)
            || registration.ResultType != typeof(TResult))
        {
            throw new OperationTypeMismatchException(
                $"Operation '{operation.Id}' has no matching durable runtime registration.");
        }

        var canonical = registration.Canonicalize(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var existing = await database.Activities.SingleOrDefaultAsync(
            activity => activity.Workspace == caller.Workspace.Value
                && activity.Principal == caller.Principal.Value
                && activity.IdempotencyKey == idempotencyKey.Value,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.Operation, operation.Id.Value, StringComparison.Ordinal)
                || !string.Equals(existing.InputFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new IdempotencyConflictException(
                    "An idempotency key cannot be reused for a different operation or input.");
            }

            return new OperationAccepted(new BrainActivityId(existing.Id));
        }

        var activity = new ProductActivityRecord
        {
            Id = Guid.NewGuid(),
            Workspace = caller.Workspace.Value,
            Principal = caller.Principal.Value,
            Operation = operation.Id.Value,
            IdempotencyKey = idempotencyKey.Value,
            InputFingerprint = fingerprint,
            TerminalResultContract = operation.TerminalResultContract.Value,
            Status = ActivityStatus.Accepted,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        database.Activities.Add(activity);
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            database.Entry(activity).State = EntityState.Detached;
            existing = await database.Activities.SingleAsync(
                candidate => candidate.Workspace == caller.Workspace.Value
                    && candidate.Principal == caller.Principal.Value
                    && candidate.IdempotencyKey == idempotencyKey.Value,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(existing.Operation, operation.Id.Value, StringComparison.Ordinal)
                || !string.Equals(existing.InputFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new IdempotencyConflictException(
                    "An idempotency key cannot be reused for a different operation or input.");
            }

            return new OperationAccepted(new BrainActivityId(existing.Id));
        }

        var decision = policy.AuthorizeOperation(caller, operation);
        if (decision == PolicyDecision.Refused)
        {
            activity.Status = ActivityStatus.Refused;
            activity.ProblemCode = "policy-refused";
            activity.ProblemSummary = "Workspace policy refused this operation.";
            activity.UpdatedAt = timeProvider.GetUtcNow();
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new OperationAccepted(new BrainActivityId(activity.Id));
        }

        if (decision == PolicyDecision.ConfirmationRequired)
        {
            activity.Status = ActivityStatus.AwaitingConfirmation;
            activity.UpdatedAt = timeProvider.GetUtcNow();
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new OperationAccepted(new BrainActivityId(activity.Id));
        }

        activity.Status = ActivityStatus.Running;
        activity.UpdatedAt = timeProvider.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        ActivityResultReference result;
        try
        {
            result = await registration.InvokeAsync(input, caller, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity.Status = ActivityStatus.Cancelled;
            activity.ProblemCode = "cancelled";
            activity.ProblemSummary = "The operation was cancelled.";
            activity.UpdatedAt = timeProvider.GetUtcNow();
            await database.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            activity.Status = ActivityStatus.Failed;
            activity.ProblemCode = "operation-failed";
            activity.ProblemSummary = "The operation failed.";
            activity.UpdatedAt = timeProvider.GetUtcNow();
            await database.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        if (result.Contract != operation.TerminalResultContract)
        {
            throw new InvalidOperationException("The durable operation handler returned the wrong result contract.");
        }

        activity.ResultContract = result.Contract.Value;
        activity.ResultPayloadReference = result.Payload.Value;
        activity.Status = ActivityStatus.Completed;
        activity.UpdatedAt = timeProvider.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new OperationAccepted(new BrainActivityId(activity.Id));
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        };

    public Task<ActivityView> ObserveAsync(
        BrainActivityId activity,
        WorkspaceContext caller,
        CancellationToken cancellationToken)
        => projections.ObserveAsync(activity, caller, cancellationToken);
}

internal sealed class DurableProductActivityProjectionService(ProductDbContext database)
    : IProductActivityProjectionService
{
    public async Task<ActivityView> ObserveAsync(
        BrainActivityId activity,
        WorkspaceContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var record = await database.Activities.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == activity.Value,
            cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Activity '{activity}' was not found.");
        if (!string.Equals(record.Workspace, caller.Workspace.Value, StringComparison.Ordinal)
            || !string.Equals(record.Principal, caller.Principal.Value, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The caller cannot observe this activity.");
        }

        var result = record.ResultContract is null || record.ResultPayloadReference is null
            ? null
            : new ActivityResultReference(
                new Brain.Abstractions.Contracts.ContractId(record.ResultContract),
                new ActivityPayloadReference(record.ResultPayloadReference));
        var problem = record.ProblemCode is null || record.ProblemSummary is null
            ? null
            : new ActivityProblem(record.ProblemCode, record.ProblemSummary);
        return new ActivityView(
            new BrainActivityId(record.Id),
            new OperationId(record.Operation),
            record.Status,
            new Brain.Abstractions.Contracts.ContractId(record.TerminalResultContract),
            null,
            result,
            problem);
    }
}

internal sealed class DurableProductDeliveryService(ProductDbContext database, TimeProvider timeProvider)
    : IDurableProductDeliveryService
{
    public async Task<bool> EnqueueAsync(
        DurableProductDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentException.ThrowIfNullOrWhiteSpace(delivery.DeliveryKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(delivery.PayloadReference);
        var exists = await database.Deliveries.AnyAsync(
            candidate => candidate.DeliveryKey == delivery.DeliveryKey
                && candidate.Workspace == delivery.Workspace.Value,
            cancellationToken).ConfigureAwait(false);
        if (exists)
        {
            return false;
        }

        database.Deliveries.Add(new ProductDeliveryRecord
        {
            DeliveryKey = delivery.DeliveryKey,
            Workspace = delivery.Workspace.Value,
            PayloadReference = delivery.PayloadReference,
            Completed = false,
            UpdatedAt = timeProvider.GetUtcNow(),
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        })
        {
            foreach (var entry in exception.Entries)
            {
                entry.State = EntityState.Detached;
            }

            return false;
        }
    }

    public async Task CompleteAsync(
        string deliveryKey,
        WorkspaceId workspace,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryKey);
        var delivery = await database.Deliveries.SingleOrDefaultAsync(
            candidate => candidate.DeliveryKey == deliveryKey
                && candidate.Workspace == workspace.Value,
            cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The durable delivery was not found.");
        delivery.Completed = true;
        delivery.UpdatedAt = timeProvider.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> IsCompletedAsync(
        string deliveryKey,
        WorkspaceId workspace,
        CancellationToken cancellationToken)
        => database.Deliveries.AsNoTracking().AnyAsync(
            candidate => candidate.DeliveryKey == deliveryKey
                && candidate.Workspace == workspace.Value
                && candidate.Completed,
            cancellationToken);
}
