using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Capabilities;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
namespace DigitalBrain.Kernel.Runtime;

[GrainType("digitalbrain.runtime.ino-effect-plan.v1")]
internal sealed class InoEffectPlanNeuron(
    [PersistentState("ino-effect-plan", RuntimeStateStorageProviders.Conversations)]
    IPersistentState<EncryptedRuntimeStateEnvelope> persistentState,
    EncryptedRuntimeStateProtector protector,
    TimeProvider timeProvider,
    InoEffectPlanAuthority authority,
    IEnumerable<IInoEffectHandler> handlers,
    ILogger<InoEffectPlanNeuron> logger) : Grain, IInoEffectPlanNeuron, IRemindable
{
    private const string ExpiryReminderName = "ino.effect-plan.expire.v1";
    private static readonly TimeSpan ExpiryReminderPeriod = TimeSpan.FromHours(1);
    private readonly IReadOnlyDictionary<string, IInoEffectHandler> _handlers = Register(handlers);
    private EncryptedPersistentState<InoEffectPlanState>? _state;
    private IGrainReminder? _expiryReminder;
    private string PlanId => this.GetPrimaryKeyString() ?? throw new InvalidOperationException("INO effect plan grains require a string key.");
    private EncryptedPersistentState<InoEffectPlanState> State => _state ??= new(
        persistentState,
        protector,
        PlanId,
        RuntimeStateKinds.InoEffectPlan,
        RuntimeStateSchemas.InoEffectPlan,
        InoEffectPlanState.Empty,
        static value => value.Revision,
        InoEffectPlanTransitions.ValidateState);
    public async Task PutAsync(InoEffectPlan plan)
    {
        var current = await State.ReadAsync();
        if (current.Plan is not null)
        {
            _ = InoEffectPlanTransitions.Put(current, PlanId, plan);
            await EnsureExpiryReminderAsync(plan.ExpiresAt);
            return;
        }
        await EnsureExpiryReminderAsync(plan.ExpiresAt);
        try
        {
            await State.UpdateAsync(
                current.Revision,
                state => InoEffectPlanTransitions.Put(state, PlanId, plan));
        }
        catch (PersistedStateWriteOutcomeUnknownException)
        {
            throw;
        }
        catch
        {
            await TryStopExpiryReminderAsync();
            throw;
        }
    }
    public async Task<InoToolEffectResult> ExecuteAsync(
        string actorScope,
        string operationId,
        string toolId,
        string summaryDigest,
        string effectId,
        string providerIdempotencyKey,
        string executionProof,
        CancellationToken cancellationToken = default)
    {
        var current = await State.ReadAsync(cancellationToken);
        var plan = current.Plan ?? throw new RuntimeStateIntegrityException("effect plan is missing");
        if (!string.Equals(plan.PlanId, PlanId, StringComparison.Ordinal) ||
            !string.Equals(plan.ActorScope, actorScope, StringComparison.Ordinal) ||
            !string.Equals(plan.OperationId, operationId, StringComparison.Ordinal) ||
            !string.Equals(plan.ToolId, toolId, StringComparison.Ordinal) ||
            !InoEffectPlanAuthority.MatchesSummary(plan.SafeSummary, summaryDigest) ||
            !authority.ValidateExecutionProof(executionProof, PlanId, actorScope, operationId, toolId, effectId, providerIdempotencyKey))
            throw new RuntimeStateIntegrityException("effect plan execution binding is invalid");
        if (current.Completion is { } completed)
            return new InoToolEffectResult(completed.Disposition, completed.SafeResult);
        InoToolEffectResult result;
        if (plan.ExpiresAt <= timeProvider.GetUtcNow())
        {
            result = new InoToolEffectResult(InoToolEffectDisposition.Failed, "This approval expired before execution. No external action was performed.");
        }
        else
        {
            try
            {
                result = await ExecuteRegisteredEffectAsync(plan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result = new InoToolEffectResult(InoToolEffectDisposition.OutcomeUnknown, "The approved external action timed out before its result could be confirmed.");
            }
            catch (Exception ex)
            {
                logger.LogWarning("INO effect plan {PlanId} failed with {ExceptionType} after execution began.", PlanId, ex.GetType().Name);
                result = new InoToolEffectResult(InoToolEffectDisposition.OutcomeUnknown, "The approved external action could not be confirmed. Review it before trying again.");
            }
        }
        var completion = new InoEffectPlanCompletion(result.Disposition, result.SafeResult);
        await State.UpdateAsync(
            current.Revision,
            state => InoEffectPlanTransitions.Complete(state, completion),
            CancellationToken.None);
        await TryStopExpiryReminderAsync();
        return result;
    }
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, ExpiryReminderName, StringComparison.Ordinal)) return;
        var current = await State.ReadAsync();
        if (current.Completion is not null)
        {
            await StopExpiryReminderAsync();
            return;
        }
        var plan = current.Plan;
        if (plan is null)
        {
            await StopExpiryReminderAsync();
            return;
        }
        if (plan.ExpiresAt > timeProvider.GetUtcNow())
        {
            _expiryReminder = await this.RegisterOrUpdateReminder(ExpiryReminderName, ReminderDueTime(plan.ExpiresAt), ExpiryReminderPeriod);
            return;
        }
        await State.UpdateAsync(
            current.Revision,
            state => InoEffectPlanTransitions.Complete(state, new InoEffectPlanCompletion(InoToolEffectDisposition.Failed, "This approval expired. No external action was performed.")));
        await StopExpiryReminderAsync();
    }
    private TimeSpan ReminderDueTime(DateTimeOffset expiresAt)
    {
        var due = expiresAt - timeProvider.GetUtcNow();
        return due > TimeSpan.FromMinutes(1) ? due : TimeSpan.FromMinutes(1);
    }
    private async Task EnsureExpiryReminderAsync(DateTimeOffset expiresAt) =>
        _expiryReminder ??= await this.RegisterOrUpdateReminder(ExpiryReminderName, ReminderDueTime(expiresAt), ExpiryReminderPeriod);
    private async Task TryStopExpiryReminderAsync()
    {
        try
        {
            await StopExpiryReminderAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning("INO effect plan {PlanId} could not remove its expiry reminder after durable completion: {ExceptionType}.", PlanId, ex.GetType().Name);
        }
    }
    private async Task StopExpiryReminderAsync()
    {
        _expiryReminder ??= await this.GetReminder(ExpiryReminderName);
        if (_expiryReminder is null) return;
        await this.UnregisterReminder(_expiryReminder);
        _expiryReminder = null;
    }
    private Task<InoToolEffectResult> ExecuteRegisteredEffectAsync(InoEffectPlan plan, CancellationToken cancellationToken) =>
        _handlers.TryGetValue(plan.ToolId, out var handler)
            ? handler.ApplyAsync(plan.ActorScope, plan.PayloadUtf8, cancellationToken)
            : throw new RuntimeStateIntegrityException("effect plan tool is not registered");
    private static IReadOnlyDictionary<string, IInoEffectHandler> Register(IEnumerable<IInoEffectHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var registered = new Dictionary<string, IInoEffectHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentException.ThrowIfNullOrWhiteSpace(handler.ToolId);
            if (!registered.TryAdd(handler.ToolId, handler))
                throw new InvalidOperationException($"Effect handler '{handler.ToolId}' is registered more than once.");
        }
        return registered;
    }
}
