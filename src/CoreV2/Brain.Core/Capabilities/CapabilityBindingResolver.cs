using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Identity;

namespace Brain.Core.Capabilities;

public sealed class CapabilityBindingNotFoundException(string message) : InvalidOperationException(message);

public sealed class CapabilityTypeMismatchException(string message) : ArgumentException(message);

internal interface ICapabilityBinding
{
    CapabilityDescriptor Descriptor { get; }
}

internal interface ICapabilityBinding<TRequest, TResult> : ICapabilityBinding
    where TRequest : class
    where TResult : class
{
    Task<TResult> InvokeAsync(TRequest request, CancellationToken cancellationToken);
}

internal sealed class CapabilityBinding<TRequest, TResult>(
    CapabilityDescriptor descriptor,
    Func<TRequest, CancellationToken, Task<TResult>> invoke) : ICapabilityBinding<TRequest, TResult>
    where TRequest : class
    where TResult : class
{
    public CapabilityDescriptor Descriptor { get; } = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

    private readonly Func<TRequest, CancellationToken, Task<TResult>> _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));

    public Task<TResult> InvokeAsync(TRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _invoke(request, cancellationToken);
    }
}

internal static class CapabilityBinding
{
    internal static ICapabilityBinding For<TRequest, TResult>(
        CapabilityDescriptor descriptor,
        Func<TRequest, CancellationToken, Task<TResult>> invoke)
        where TRequest : class
        where TResult : class
        => new CapabilityBinding<TRequest, TResult>(descriptor, invoke);
}

internal sealed class CapabilityBindingResolver
{
    private readonly IReadOnlyDictionary<CapabilityId, ICapabilityBinding> _bindings;

    internal CapabilityBindingResolver(IEnumerable<ICapabilityBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var materialized = bindings.ToArray();
        _bindings = materialized.ToDictionary(static binding => binding.Descriptor.Id);
        if (_bindings.Count != materialized.Length)
        {
            throw new ArgumentException("Capability bindings must be unique per capability.", nameof(bindings));
        }
    }

    internal ICapabilityBinding<TRequest, TResult> Resolve<TRequest, TResult>(CapabilityDescriptor capability)
        where TRequest : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (!_bindings.TryGetValue(capability.Id, out var binding))
        {
            throw new CapabilityBindingNotFoundException(
                $"Capability '{capability.Id}' has no explicit binding.");
        }

        if (!Equivalent(binding.Descriptor, capability))
        {
            throw new CapabilityBindingNotFoundException(
                $"Capability binding '{capability.Id}' does not match its registered descriptor.");
        }

        return binding as ICapabilityBinding<TRequest, TResult>
            ?? throw new CapabilityTypeMismatchException(
                $"Capability '{capability.Id}' is not bound for request '{typeof(TRequest).FullName}' "
                + $"and result '{typeof(TResult).FullName}'.");
    }

    internal static bool Equivalent(CapabilityDescriptor left, CapabilityDescriptor right)
        => left.Id == right.Id
            && left.RequestContract == right.RequestContract
            && left.ResultContract == right.ResultContract
            && left.Owner == right.Owner
            && left.Version == right.Version;
}
