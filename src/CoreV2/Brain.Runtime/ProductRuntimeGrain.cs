using System.Security.Cryptography;
using System.Text;
using Brain.Runtime.Abstractions;

namespace Brain.Runtime;

public sealed class ProductRuntimeGrain(
    IEnumerable<IRuntimeProductModule> modules,
    IGrainFactory grainFactory) : Grain, IProductRuntimeGrain
{
    private readonly IReadOnlyList<IRuntimeProductModule> _modules = ValidateModules(modules);
    private readonly IGrainFactory _grainFactory = grainFactory;

    public Task<IReadOnlyList<RuntimeModuleDescriptor>> GetModulesAsync()
        => Task.FromResult<IReadOnlyList<RuntimeModuleDescriptor>>(
            _modules.Select(static module => module.Module).ToArray());

    public Task<IReadOnlyList<RuntimeOperationDescriptor>> GetOperationsAsync()
        => Task.FromResult<IReadOnlyList<RuntimeOperationDescriptor>>(
            _modules
                .Where(static module => module.Module.Status == RuntimeModuleStatus.Ready)
                .SelectMany(static module => module.Operations)
                .OrderBy(static operation => operation.Id, StringComparer.Ordinal)
                .ToArray());

    public Task<RuntimeActivityReceipt> InvokeAsync(RuntimeInvocation invocation)
    {
        ValidateInvocation(invocation);
        var activity = ActivityId(invocation.Workspace, invocation.Principal, invocation.IdempotencyKey);
        return _grainFactory.GetGrain<IProductActivityGrain>(activity).StartAsync(invocation);
    }

    internal static Guid ActivityId(string workspace, string principal, string idempotencyKey)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{workspace}\0{principal}\0{idempotencyKey}"));
        var id = new Guid(bytes.AsSpan(0, 16));
        return id == Guid.Empty ? new Guid(bytes.AsSpan(16, 16)) : id;
    }

    private static IReadOnlyList<IRuntimeProductModule> ValidateModules(
        IEnumerable<IRuntimeProductModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var copy = modules.OrderBy(static module => module.Module.Id, StringComparer.Ordinal).ToArray();
        if (copy.Any(static module => string.IsNullOrWhiteSpace(module.Module.Id))
            || copy.Select(static module => module.Module.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new InvalidOperationException("Runtime product module ids must be non-empty and unique.");
        }

        var operations = copy.SelectMany(static module => module.Operations).ToArray();
        if (operations.Any(static operation => string.IsNullOrWhiteSpace(operation.Id))
            || operations.Select(static operation => operation.Id).Distinct(StringComparer.Ordinal).Count()
                != operations.Length
            || operations.Any(operation => copy.All(module => module.Module.Id != operation.ModuleId)))
        {
            throw new InvalidOperationException(
                "Runtime product operations must be unique and belong to an installed module.");
        }

        return copy;
    }

    private static void ValidateInvocation(RuntimeInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (string.IsNullOrWhiteSpace(invocation.OperationId)
            || string.IsNullOrWhiteSpace(invocation.InputJson)
            || string.IsNullOrWhiteSpace(invocation.Workspace)
            || string.IsNullOrWhiteSpace(invocation.Principal)
            || string.IsNullOrWhiteSpace(invocation.IdempotencyKey))
        {
            throw new ArgumentException("A runtime invocation requires operation, input, caller, and idempotency key.");
        }
    }
}
