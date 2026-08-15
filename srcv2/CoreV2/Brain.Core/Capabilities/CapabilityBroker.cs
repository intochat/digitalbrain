using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Policy;
using Brain.Core.Modules;

namespace Brain.Core.Capabilities;

public sealed class MissingActivityContextException(string message) : ArgumentException(message);

public sealed class CapabilityNotDelegatedException(string message) : UnauthorizedAccessException(message);

public sealed class CapabilityNotInstalledException : InvalidOperationException
{
    public CapabilityNotInstalledException(string message)
        : base(message)
    {
    }

    public CapabilityNotInstalledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class CapabilityPolicyRefusedException(string message) : UnauthorizedAccessException(message);

internal sealed class CapabilityBroker : ICapabilityBroker
{
    private readonly IModuleRegistry _registry;
    private readonly IWorkspacePolicyEvaluator _policy;
    private readonly CapabilityBindingResolver _bindings;
    private readonly CapabilityUseState _state;

    internal CapabilityBroker(
        IModuleRegistry registry,
        IWorkspacePolicyEvaluator policy,
        CapabilityBindingResolver bindings,
        CapabilityUseState state)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public Task<TResult> UseAsync<TRequest, TResult>(
        CapabilityDescriptor capability,
        CapabilityUseName useName,
        TRequest request,
        ActivityContext context,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(useName.Value, nameof(useName));
        VerifyActivityContext(context);
        cancellationToken.ThrowIfCancellationRequested();

        var registered = ResolveInstalled(capability);
        if (!context.Delegation.Capabilities.Contains(registered.Id))
        {
            throw new CapabilityNotDelegatedException(
                $"Activity '{context.Activity}' cannot use capability '{registered.Id}'.");
        }

        if (_policy.AuthorizeCapability(context, registered) != PolicyDecision.Allowed)
        {
            throw new CapabilityPolicyRefusedException(
                $"Workspace policy refused capability '{registered.Id}'.");
        }

        var binding = _bindings.Resolve<TRequest, TResult>(registered);
        var key = new CapabilityUseKey(context.Activity, registered.Id, useName);
        return _state.UseAsync(key, () => binding.InvokeAsync(request, cancellationToken));
    }

    private CapabilityDescriptor ResolveInstalled(CapabilityDescriptor capability)
    {
        CapabilityDescriptor registered;
        try
        {
            registered = _registry.GetCapability(capability.Id);
        }
        catch (KeyNotFoundException error)
        {
            throw new CapabilityNotInstalledException(
                $"The active workspace module set does not provide capability '{capability.Id}'.", error);
        }

        if (!CapabilityBindingResolver.Equivalent(registered, capability))
        {
            throw new CapabilityNotInstalledException(
                $"The active workspace module set does not provide the requested descriptor for capability '{capability.Id}'.");
        }

        var provider = _registry.Get(registered.Owner);
        if (!provider.ProvidedCapabilities.Contains(registered))
        {
            throw new CapabilityNotInstalledException(
                $"Capability '{registered.Id}' is not published by its registered provider '{registered.Owner}'.");
        }

        return registered;
    }

    private static void VerifyActivityContext(ActivityContext? context)
    {
        if (context is null
            || context.Workspace.IsEmpty
            || string.IsNullOrWhiteSpace(context.Principal.Value)
            || context.Activity.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(context.Correlation.Value))
        {
            throw new MissingActivityContextException("Capability use requires a verified active activity context.");
        }
    }
}
