using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Compute;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>
/// The capability caller the host stamped on the current call (D-15). A trusted edge (the os kernel's HTTP and app proxy
/// edges) stamps it with <see cref="Stamp"/>; it flows with the call through Orleans <see cref="RequestContext"/> into grain
/// calls. Capability implementations read <see cref="Current"/> and pass it explicitly to their port; apps cannot name a
/// caller. Separate from the calling-neuron provenance (<c>db.caller</c>).
/// </summary>
public static class RuntimeCallerContext
{
    /// <summary>The Orleans RequestContext key for the principal.</summary>
    public const string RequestContextKey = "db.cap.principal";

    /// <summary>The Orleans RequestContext key for trusted internal module calls.</summary>
    public const string ModuleCallKey = "intochatos.trusted-module-call";

    private const string PrincipalKey = RequestContextKey;
    private const string AccountKey = "db.cap.account";
    private const string BrainKey = "db.cap.brain";
    private const string AppKey = "db.cap.app";
    private const string ConversationKey = "db.cap.conversation";

    private static readonly string[] Keys = [PrincipalKey, AccountKey, BrainKey, AppKey, ConversationKey];

    /// <summary>The stamped caller of the current call, or null when nothing is stamped.</summary>
    public static CapabilityCaller? Current
    {
        get
        {
            var principal = RequestContext.Get(PrincipalKey) as string;
            var account = RequestContext.Get(AccountKey) as string;
            var brain = RequestContext.Get(BrainKey) as string;
            if (string.IsNullOrWhiteSpace(principal) || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(brain)) { return null; }
            return new CapabilityCaller(principal, account, brain, RequestContext.Get(AppKey) as string, RequestContext.Get(ConversationKey) as string);
        }
    }

    /// <summary>The stamped caller.</summary>
    /// <exception cref="UnauthorizedAccessException">No caller is stamped on the call; capabilities deny rather than default.</exception>
    public static CapabilityCaller Required(string capability)
        => Current ?? throw new UnauthorizedAccessException($"{capability}: the call carries no stamped caller.");

    /// <summary>Stamps <paramref name="caller"/> on the current async flow until the result is disposed (then restores the previous caller).</summary>
    public static IDisposable Stamp(CapabilityCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller.PrincipalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller.AccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller.BrainId);
        var previous = Keys.Select(RequestContext.Get).ToArray();
        Write([caller.PrincipalId, caller.AccountId, caller.BrainId, caller.AppId, caller.ConversationId]);
        return new Restore(previous);
    }

    /// <summary>Validates that <paramref name="context"/> is trusted, stamps it on the current flow, and starts an edge trace if none is active.</summary>
    public static IDisposable StampScope(CapabilityCaller context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsTrusted(context))
        {
            throw new UntrustedCallerException(
                $"Caller kind {context.Kind} cannot be stamped by {context.StampedBy}.");
        }

        var scope = Stamp(context);
        if (TraceContext.Current is null)
        {
            var origin = new NeuronId(new BrainId(context.BrainId), "edge", "request");
            var traceScope = TraceContext.Enter(TraceContext.Begin(context.AccountId, context.BrainId, origin));
            return new CombinedScope(scope, traceScope);
        }
        return scope;
    }

    /// <summary>Gets the currently stamped caller if present.</summary>
    public static bool TryGet([NotNullWhen(true)] out CapabilityCaller? context)
    {
        context = Current;
        return context is not null;
    }

    /// <summary>Returns the currently stamped caller or throws <see cref="UntrustedCallerException"/>.</summary>
    public static CapabilityCaller Require()
        => TryGet(out var context)
            ? context
            : throw new UntrustedCallerException("No caller context was stamped at a trusted edge.");

    /// <summary>Returns true when <paramref name="caller"/> originated from a model turn.</summary>
    public static bool IsModelOriginated(CapabilityCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return caller.ConversationId is not null;
    }

    /// <summary>Returns true when <paramref name="context"/> carries all required fields and valid enum values.</summary>
    public static bool IsTrusted(CapabilityCaller context)
        => context is not null
            && !string.IsNullOrWhiteSpace(context.PrincipalId)
            && !string.IsNullOrWhiteSpace(context.AccountId)
            && !string.IsNullOrWhiteSpace(context.BrainId)
            && Enum.IsDefined(context.Kind)
            && Enum.IsDefined(context.StampedBy);

    private static void Write(object?[] values)
    {
        for (var i = 0; i < Keys.Length; i++)
        {
            if (values[i] is null) { RequestContext.Remove(Keys[i]); }
            else { RequestContext.Set(Keys[i], values[i]!); }
        }
    }

    private sealed class Restore(object?[] previous) : IDisposable
    {
        public void Dispose() => Write(previous);
    }

    private sealed class CombinedScope(IDisposable callerScope, IDisposable traceScope) : IDisposable
    {
        public void Dispose()
        {
            traceScope.Dispose();
            callerScope.Dispose();
        }
    }
}

/// <summary>Thrown when a call requires a trusted edge caller stamp and none (or an invalid one) is present.</summary>
[GenerateSerializer, Alias("enforcement.untrusted-caller")]
public sealed class UntrustedCallerException : Exception
{
    /// <summary>Creates the exception with its message.</summary>
    public UntrustedCallerException(string message) : base(message) { }

    /// <summary>Creates the exception with its message and inner cause.</summary>
    public UntrustedCallerException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Creates the exception.</summary>
    public UntrustedCallerException() { }
}
