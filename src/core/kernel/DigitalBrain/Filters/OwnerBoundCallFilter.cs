using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal sealed class OwnerBoundCallFilter(IEnumerable<ReminderSourceAllowlist> additionalReminderSources) :
    IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsReminderCall(context))
        {
            if (IsTrustedReminderProvider(context.SourceId))
            {
                await context.Invoke();
                return;
            }

            throw new NeuronAuthorizationException(
                $"'{nameof(IRemindable.ReceiveReminder)}' can be called only by the Orleans reminder provider. Source: '{context.SourceId?.ToString() ?? "unattributed"}'.");
        }

        if (IsOutboxDrainCall(context))
        {
            RequireDedicatedWakeupForTarget(context);
            await context.Invoke();
            return;
        }

        if (context.Grain is Neuron enumerationTarget
            && CapabilityInvocation.IsEnumerationDispatch(context.InterfaceMethod)
            && CapabilityInvocation.EnumerationId(context.Request) is { } enumerationId
            && (CapabilityInvocation.IsEnumerationContinuation(context.InterfaceMethod)
                || CapabilityInvocation.IsEnumerationDisposal(context.InterfaceMethod)))
        {
            enumerationTarget.RequireStreamedEnumerationInitiator(enumerationId, context.SourceId);
            RequireSameOwnerWhenAttributed(context, enumerationTarget);

            if (CapabilityInvocation.IsEnumerationDisposal(context.InterfaceMethod))
            {
                try
                {
                    await context.Invoke();
                }
                finally
                {
                    enumerationTarget.ReleaseStreamedEnumeration(enumerationId);
                }

                return;
            }

            await context.Invoke();
            return;
        }

        if (OwnerOf(context.SourceId) is not { } caller)
        {
            if (context.Grain is Neuron unattributedTarget && !AllowsUnattributedCaller(context, unattributedTarget))
            {
                throw new NeuronAuthorizationException(
                    $"'{context.InterfaceMethod?.Name}' is not a client entry point, so an unattributed caller cannot be authorized to reach '{unattributedTarget.Id}'. Reach a neuron through a session of the owner you are acting as.");
            }
        }
        else if (context.Grain is Neuron target && caller != target.Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{target.Id}' belongs to owner '{target.Id.Owner}' and cannot be reached by owner '{caller}'.");
        }

        if (context.Grain is Neuron bindTarget
            && CapabilityInvocation.IsEnumerationStart(context.InterfaceMethod)
            && CapabilityInvocation.EnumerationId(context.Request) is { } startedEnumeration)
        {
            await context.Invoke();
            bindTarget.BindStreamedEnumeration(startedEnumeration, context.SourceId);
            return;
        }

        await context.Invoke();
    }

    private static void RequireSameOwnerWhenAttributed(IIncomingGrainCallContext context, Neuron target)
    {
        if (OwnerOf(context.SourceId) is { } caller && caller != target.Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{target.Id}' belongs to owner '{target.Id.Owner}' and cannot be reached by owner '{caller}'.");
        }
    }

    private static bool IsReminderCall(IIncomingGrainCallContext context)
        => context.InterfaceMethod?.DeclaringType == typeof(IRemindable)
            && context.InterfaceMethod?.Name == nameof(IRemindable.ReceiveReminder);

    private static bool IsOutboxDrainCall(IIncomingGrainCallContext context)
        => context.InterfaceMethod?.DeclaringType == typeof(IOutboxDrain)
            && context.InterfaceMethod?.Name == nameof(IOutboxDrain.Drain);

    private static void RequireDedicatedWakeupForTarget(IIncomingGrainCallContext context)
    {
        if (context.SourceId is not { } source
            || !string.Equals(source.Type.ToString(), OutboxWakeup.GrainTypeName, StringComparison.Ordinal)
            || !OutboxWakeup.TryParseTarget(source.Key.ToString(), out var encodedTarget)
            || encodedTarget.ToGrainId() != context.TargetId)
        {
            throw new NeuronAuthorizationException(
                $"'{nameof(IOutboxDrain.Drain)}' can be called only by the dedicated wakeup for target '{context.TargetId}'. Source: '{context.SourceId?.ToString() ?? "unattributed"}'.");
        }
    }

    private bool IsTrustedReminderProvider(GrainId? source)
        => source is { } identified
            && (string.Equals(identified.Type.ToString(), ReminderGrainServiceType, StringComparison.Ordinal)
                || IsAdditionalTrustedSource(identified));

    private bool IsAdditionalTrustedSource(GrainId source)
        => additionalReminderSources.Any(allowlist => allowlist.Contains(source));

    private static bool AllowsUnattributedCaller(IIncomingGrainCallContext context, Neuron target)
        => IsClientEntryPoint(context.InterfaceMethod)
            || IsClientEntryPoint(ContractMethodTheTargetImplements(context, target));

    private static MethodInfo? ContractMethodTheTargetImplements(IIncomingGrainCallContext context, Neuron target)
        => CapabilityInvocation.ContractMethod(context.InterfaceMethod, context.Request) is { } contract
            && contract.DeclaringType!.IsInstanceOfType(target)
                ? contract
                : null;

    private static bool IsClientEntryPoint(MethodInfo? method)
        => method?.DeclaringType?.GetCustomAttribute<ClientEntryPointAttribute>() is not null;

    private static OwnerId? OwnerOf(GrainId? source)
    {
        if (source?.Key.ToString() is not { } key)
        {
            return null;
        }

        var separator = key.IndexOf(IdentityPartSeparator, StringComparison.Ordinal);

        return separator <= 0 ? null : new OwnerId(key[..separator]);
    }

    private const string ReminderGrainServiceType = "sys.svc.user.36F5F3BF";
    private const char IdentityPartSeparator = '/';
}
