using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Capabilities;

namespace DigitalBrain.Brains;

public static class CallerAdmission
{
    private const string PrincipalKey = "db.cap.principal";
    private const string AccountKey = "db.cap.account";
    private const string BrainKey = "db.cap.brain";
    private const string AppKey = "db.cap.app";
    private const string ConversationKey = "db.cap.conversation";

    internal static readonly AsyncLocal<CapabilityCaller?> Ambient = new();

    internal static Func<CapabilityCaller?>? Reader { get; set; }

    private static Func<string, object?>? s_requestContextGet;

    internal static IDisposable Stamp(CapabilityCaller? caller)
    {
        var previous = Ambient.Value;
        Ambient.Value = caller;
        return new Scope(previous);
    }

    internal static bool TryGetCaller([NotNullWhen(true)] out CapabilityCaller? caller)
    {
        if (Ambient.Value is { } local)
        {
            caller = local;
            return true;
        }

        if (Reader?.Invoke() is { } read)
        {
            caller = read;
            return true;
        }

        var get = RequestContextGet();
        if (get is not null)
        {
            var principal = get(PrincipalKey) as string;
            var account = get(AccountKey) as string;
            var brain = get(BrainKey) as string;
            if (!string.IsNullOrWhiteSpace(principal) && !string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(brain))
            {
                caller = new CapabilityCaller(principal, account, brain, get(AppKey) as string, get(ConversationKey) as string);
                return true;
            }
        }

        caller = null;
        return false;
    }

    public static void RequireSameBrain(string brainId, string? appId = null, string? target = null)
    {
        if (!TryGetCaller(out var caller)) { return; }
        var sameBrain = string.Equals(caller.BrainId, brainId, StringComparison.Ordinal);
        var admitted = caller.Kind switch
        {
            CallerKind.Platform => true,
            CallerKind.User or CallerKind.Agent => sameBrain,
            CallerKind.App => appId is not null && sameBrain && string.Equals(caller.AppId, appId, StringComparison.Ordinal),
            _ => false,
        };
        if (!admitted)
        {
            throw new UnauthorizedAccessException($"A {caller.Kind} caller of brain {caller.BrainId} may not touch {target ?? brainId}.");
        }
    }

    public static void RequirePerson(string message)
    {
        var caller = TryGetCaller(out var stamped) ? stamped : throw new UnauthorizedAccessException("A circle acts only on a stamped caller.");
        if (caller.Kind == CallerKind.App || caller.StampedBy == TrustedEdge.AppProxy || AppPrincipal.IsApp(caller.PrincipalId))
        {
            throw new UnauthorizedAccessException(message);
        }
    }

    public static void RequireBrainMember(string brainId, string message)
    {
        if (!TryGetCaller(out var caller))
        {
            throw new UnauthorizedAccessException("No caller context was stamped at a trusted edge.");
        }
        if (caller.Kind == CallerKind.App || caller.StampedBy == TrustedEdge.AppProxy || AppPrincipal.IsApp(caller.PrincipalId)
            || BrainScope.Create(caller.AccountId, caller.BrainId).Id != brainId)
        {
            throw new UnauthorizedAccessException(message);
        }
    }

    public static void RequireNotAppCaller(string message)
    {
        if (TryGetCaller(out var caller)
            && (caller.Kind == CallerKind.App || caller.StampedBy == TrustedEdge.AppProxy || AppPrincipal.IsApp(caller.PrincipalId)))
        {
            throw new UnauthorizedAccessException(message);
        }
    }

    private static Func<string, object?>? RequestContextGet()
    {
        if (s_requestContextGet is not null) { return s_requestContextGet; }
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, "Orleans.Core.Abstractions", StringComparison.Ordinal)) { continue; }
            var method = assembly.GetType("Orleans.Runtime.RequestContext")?.GetMethod("Get", [typeof(string)]);
            if (method is not null)
            {
                s_requestContextGet = method.CreateDelegate<Func<string, object?>>();
                return s_requestContextGet;
            }
        }
        return null;
    }

    private sealed class Scope(CapabilityCaller? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
