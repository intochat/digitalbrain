using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Capabilities;

namespace DigitalBrain.Brains;

public static class CallerAdmission
{
    internal static readonly AsyncLocal<CapabilityCaller?> Ambient = new();

    internal static Func<CapabilityCaller?>? Reader { get; set; }

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

        caller = null;
        return false;
    }

    public static void RequireSameBrain(string brainId, string? appId = null, string? target = null)
    {
        if (!TryGetCaller(out var caller)) { return; }
        if (!caller.CanTouchBrain(brainId, appId))
        {
            throw new UnauthorizedAccessException($"A {caller.Kind} caller of brain {caller.BrainId} may not touch {target ?? brainId}.");
        }
    }

    public static void RequirePerson(string message)
    {
        var caller = TryGetCaller(out var stamped) ? stamped : throw new UnauthorizedAccessException("A circle acts only on a stamped caller.");
        if (caller.IsAppCaller)
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
        if (!caller.IsNonAppMemberOf(brainId))
        {
            throw new UnauthorizedAccessException(message);
        }
    }

    public static void RequireNotAppCaller(string message)
    {
        if (TryGetCaller(out var caller) && caller.IsAppCaller)
        {
            throw new UnauthorizedAccessException(message);
        }
    }

    private sealed class Scope(CapabilityCaller? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
