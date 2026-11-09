using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Access;
using DigitalBrain.Brains;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.Runtime.Orleans.Enforcement;

/// <summary>
/// Grain call filter enforcing access rules on plain neurons hosted on Orleans grains.
/// Checks caller stamps, method access attributes ([Public], [Internal], [Shared], [PerAccount]),
/// and delegates to <see cref="ICallFilter"/> for authorization.
/// </summary>
public sealed class PlainNeuronCallFilter(ICallFilter filter, ILogger<PlainNeuronCallFilter> logger) : IIncomingGrainCallFilter
{
    private static readonly ConcurrentDictionary<(string Contract, string Method, int Arity), AccessAttribute?> Policies = new();
    private static readonly ConcurrentDictionary<string, Type?> Contracts = new(StringComparer.Ordinal);

    private static readonly Action<ILogger, string, string, Exception?> LogAccessDeniedUnstamped =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(1, nameof(Invoke)), "Access denied: unstamped call to plain neuron {Contract}.{Method}");

    private static readonly Action<ILogger, string, string, string, string, string?, Exception?> LogAccessDeniedPolicy =
        LoggerMessage.Define<string, string, string, string, string?>(LogLevel.Warning, new EventId(2, nameof(Authorize)), "Access denied: {Principal} in brain {BrainId} calling {Operation} on plain neuron {Target}: {Explanation}");

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is not INeuronHost || context.Request.GetArgumentCount() == 0 || context.Request.GetArgument(0) is not RuntimeInvocation call)
        {
            await context.Invoke();
            return;
        }

        if (IsPlumbing(call))
        {
            await context.Invoke();
            return;
        }

        if (!RuntimeCallerContext.TryGet(out var caller) || caller is null)
        {
            if (context.SourceId is { } internalSource && !internalSource.Type.ToString()!.StartsWith("sys.client", StringComparison.Ordinal))
            {
                await context.Invoke();
                return;
            }

            LogAccessDeniedUnstamped(logger, call.Contract, call.Method, null);
            throw new UnauthorizedAccessException($"Unstamped calls are not authorized for plain neuron {call.Contract}.{call.Method}");
        }

        var id = DigitalBrain.NeuronId.Parse(context.TargetId.Key.ToString()!);
        var (scope, isPublic) = Classify(caller, id, call);
        var internalCall = global::Orleans.Runtime.RequestContext.Get(RuntimeCallerContext.ModuleCallKey) is true
            || context.SourceId is { } source && !source.Type.ToString()!.StartsWith("sys.client", StringComparison.Ordinal);

        if (scope is null && !isPublic && internalCall)
        {
            await context.Invoke();
            return;
        }

        await Authorize(filter, logger, caller, id, call);
        var previousModuleCall = global::Orleans.Runtime.RequestContext.Get(RuntimeCallerContext.ModuleCallKey);
        global::Orleans.Runtime.RequestContext.Set(RuntimeCallerContext.ModuleCallKey, true);
        try
        {
            await context.Invoke();
        }
        finally
        {
            if (previousModuleCall is not null)
            {
                global::Orleans.Runtime.RequestContext.Set(RuntimeCallerContext.ModuleCallKey, previousModuleCall);
            }
            else
            {
                global::Orleans.Runtime.RequestContext.Remove(RuntimeCallerContext.ModuleCallKey);
            }
        }
    }

    private static (string? Scope, bool IsPublic) Classify(CapabilityCaller caller, DigitalBrain.NeuronId id, RuntimeInvocation call) => Policy(call) switch
    {
        PublicAttribute => (null, true),
        InternalAttribute => (null, false),
        SharedAttribute => (BrainScope.CurrentId(), false),
        PerAccountAttribute => (caller.AccountId == id.Key ? BrainScope.Create(caller.AccountId, caller.BrainId).Id : null, false),
        _ => (id.Brain.Value, false),
    };

    /// <summary>Authorizes a call under the enforcement filter.</summary>
    /// <exception cref="UnauthorizedAccessException">The policy refuses the call.</exception>
    public static async Task Authorize(ICallFilter filter, ILogger logger, CapabilityCaller caller, DigitalBrain.NeuronId id, RuntimeInvocation call)
    {
        if (IsPlumbing(call)) { return; }
        var (scope, isPublic) = Classify(caller, id, call);
        var target = $"{id.Type}/{id.Brain.Value}/{id.Key}";
        var decision = await filter.AuthorizeAsync(new()
        {
            TargetNeuron = target,
            Operation = call.Method,
            EnforceTarget = true,
            TargetScope = scope,
            PublicOperation = isPublic,
        });

        if (!decision.Allowed)
        {
            LogAccessDeniedPolicy(logger, caller.PrincipalId, caller.BrainId, call.Method, id.Type, decision.Explanation, null);
            throw new UnauthorizedAccessException($"{decision.Explanation} (target {id.Type}, operation {call.Method})");
        }
    }

    internal static bool IsPlumbing(RuntimeInvocation call)
        => ContractOf(call.Contract) is { } contract && (!contract.IsVisible || contract.IsDefined(typeof(InternalAttribute), inherit: false));

    private static AccessAttribute? Policy(RuntimeInvocation call) => Policies.GetOrAdd((call.Contract, call.Method, call.Arguments.Length), static key =>
    {
        if (ContractOf(key.Contract) is not { } contract) { return null; }
        var method = contract.GetMethods().FirstOrDefault(candidate => candidate.Name == key.Method && candidate.GetParameters().Length == key.Arity);
        return method?.GetCustomAttribute<AccessAttribute>(inherit: false) ?? contract.GetCustomAttribute<AccessAttribute>(inherit: false);
    });

    internal static Type? ContractOf(string name)
    {
        if (Contracts.TryGetValue(name, out var known)) { return known; }
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic).ToArray();
        var found = assemblies.Select(assembly => assembly.GetType(name, throwOnError: false)).FirstOrDefault(type => type is { IsInterface: true })
            ?? assemblies.Where(References)
                .SelectMany(Loadable).FirstOrDefault(type => type.IsInterface
                    && type.GetCustomAttribute<NeuronAliasAttribute>(inherit: false)?.Alias == name);
        if (found is not null) { Contracts.TryAdd(name, found); }
        return found;
    }

    private static bool References(Assembly assembly)
        => assembly.GetReferencedAssemblies().Any(reference => reference.Name == typeof(NeuronAliasAttribute).Assembly.GetName().Name);

    private static IEnumerable<Type> Loadable(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error) { return error.Types.OfType<Type>(); }
    }
}
