using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal sealed class OwnerBoundCallFilter(
    IEnumerable<ReminderSourceAllowlist> additionalReminderSources) :
    IIncomingGrainCallFilter
{
    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsReminderCall(context))
        {
            if (IsTrustedReminderProvider(context.SourceId))
            {
                return context.Invoke();
            }

            throw new NeuronAuthorizationException(
                $"'{nameof(IRemindable.ReceiveReminder)}' can be called only by the Orleans reminder provider. Source: '{context.SourceId?.ToString() ?? "unattributed"}'.");
        }

        if (OwnerOf(context.SourceId) is not { } caller)
        {
            if (context.Grain is SubscriptionRegistry unattributedRegistry)
            {
                throw new NeuronAuthorizationException(
                    $"The subscription registry of owner '{unattributedRegistry.Owner}' cannot be reached by an unattributed caller.");
            }

            if (context.Grain is Neuron unattributedTarget && !IsClientEntryPoint(context.InterfaceMethod))
            {
                throw new NeuronAuthorizationException(
                    $"'{context.InterfaceMethod?.Name}' is not a client entry point, so an unattributed caller cannot be authorized to reach '{unattributedTarget.Id}'. Reach a neuron through a session of the owner you are acting as.");
            }
        }
        else
        {
            if (context.Grain is Neuron target && caller != target.Id.Owner)
            {
                throw new NeuronAuthorizationException(
                    $"Neuron '{target.Id}' belongs to owner '{target.Id.Owner}' and cannot be reached by owner '{caller}'.");
            }

            if (context.Grain is SubscriptionRegistry registry && caller != registry.Owner)
            {
                throw new NeuronAuthorizationException(
                    $"The subscription registry of owner '{registry.Owner}' cannot be reached by owner '{caller}'.");
            }
        }

        return context.Invoke();
    }

    private static bool IsReminderCall(IIncomingGrainCallContext context)
        => context.InterfaceMethod?.DeclaringType == typeof(IRemindable)
            && context.InterfaceMethod?.Name == nameof(IRemindable.ReceiveReminder);

    private bool IsTrustedReminderProvider(GrainId? source)
        => source is { } identified
            && (string.Equals(
                    identified.Type.ToString(),
                    ReminderGrainServiceType,
                    StringComparison.Ordinal)
                || IsAdditionalTrustedSource(identified));

    private bool IsAdditionalTrustedSource(GrainId? source)
        => source is { } identified
            && additionalReminderSources.Any(
                allowlist => allowlist.Contains(identified));

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
