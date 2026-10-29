using DigitalBrain.Capabilities;
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
    private const string PrincipalKey = "db.cap.principal";
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
}
