using System.Reflection;
using DigitalBrain.Access;
using DigitalBrain.Kernel;

namespace DigitalBrain.Invocation;

/// <summary>What a call returns once awaited.</summary>
public enum InvocationResultKind
{
    /// <summary>void, Task or ValueTask: no result.</summary>
    None,

    /// <summary>A value, Task&lt;T&gt; or ValueTask&lt;T&gt;: one result.</summary>
    Value,

    /// <summary>IAsyncEnumerable&lt;T&gt;: a stream of results.</summary>
    Stream,
}

/// <summary>A contract method resolved by <see cref="NeuronBinder"/>, with its compiled invoker. Cached and shared.</summary>
public sealed class BoundCall
{
    internal BoundCall(Type contract, MethodInfo method, InvocationResultKind kind, Type? resultType, Type? taskResultType, bool isTask, Func<object, object?[], object?> invoker,
        Func<object, CancellationToken, IAsyncEnumerable<object?>>? stream)
    {
        Contract = contract;
        Method = method;
        Kind = kind;
        ResultType = resultType;
        TaskResultType = taskResultType;
        IsTask = isTask;
        Invoker = invoker;
        Streamer = stream;
        Parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        ArgumentCount = Parameters.Count(type => type != typeof(CancellationToken));
        Access = OperationAccessOf.Resolve(contract, method);
    }

    internal BoundCall(CallDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
        Contract = descriptor.Contract ?? descriptor.Method?.DeclaringType ?? typeof(object);
        Method = descriptor.Method!;
        Kind = descriptor.Kind;
        ResultType = descriptor.ResultType;
        TaskResultType = descriptor.TaskResultType;
        IsTask = descriptor.IsTask;
        Streamer = descriptor.Streamer;
        Parameters = descriptor.ParameterTypes;
        ArgumentCount = descriptor.Arity;
        Access = descriptor.Access;
        Invoker = (target, values) =>
        {
            var task = descriptor.Invoker(target, values);
            return task.GetAwaiter().GetResult();
        };
    }

    /// <summary>The pre-compiled <see cref="CallDescriptor"/>, if constructed from one.</summary>
    internal CallDescriptor? Descriptor { get; }

    /// <summary>The contract the call was bound against.</summary>
    public Type Contract { get; }

    /// <summary>The bound method.</summary>
    public MethodInfo Method { get; }

    /// <summary>The access classification for caller admission.</summary>
    public OperationAccess Access { get; }

    /// <summary>What the call returns.</summary>
    public InvocationResultKind Kind { get; }

    /// <summary>The result or stream item type, or null when the call returns nothing.</summary>
    public Type? ResultType { get; }

    /// <summary>How many JSON arguments the call takes.</summary>
    public int ArgumentCount { get; }

    internal Type[] Parameters { get; }

    // The awaited task's result type after ValueTask shapes were turned into tasks; null for no result.
    internal Type? TaskResultType { get; }

    internal bool IsTask { get; }

    internal Func<object, object?[], object?> Invoker { get; }

    internal Func<object, CancellationToken, IAsyncEnumerable<object?>>? Streamer { get; }
}
