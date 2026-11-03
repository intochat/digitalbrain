using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Capabilities;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>Compatibility shim forwarding to <see cref="RuntimeCallerContext"/>.</summary>
[Obsolete("Use RuntimeCallerContext")]
public static class CallerContextStamper
{
    /// <summary>The Orleans RequestContext key for the principal.</summary>
    public const string RequestContextKey = RuntimeCallerContext.RequestContextKey;

    /// <summary>The Orleans RequestContext key for trusted internal module calls.</summary>
    public const string ModuleCallKey = RuntimeCallerContext.ModuleCallKey;

    /// <summary>Stamps <paramref name="context"/> on the current flow and returns <paramref name="context"/>.</summary>
    public static CapabilityCaller Stamp(CapabilityCaller context)
    {
        _ = RuntimeCallerContext.StampScope(context);
        return context;
    }

    /// <summary>Stamps <paramref name="context"/> on the current flow until the returned scope is disposed.</summary>
    public static IDisposable StampScope(CapabilityCaller context) => RuntimeCallerContext.StampScope(context);

    /// <summary>Gets the currently stamped caller if present.</summary>
    public static bool TryGet([NotNullWhen(true)] out CapabilityCaller? context) => RuntimeCallerContext.TryGet(out context);

    /// <summary>Returns the currently stamped caller or throws <see cref="UntrustedCallerException"/>.</summary>
    public static CapabilityCaller Require() => RuntimeCallerContext.Require();

    /// <summary>Returns true when <paramref name="caller"/> originated from a model turn.</summary>
    public static bool IsModelOriginated(CapabilityCaller caller) => RuntimeCallerContext.IsModelOriginated(caller);

    /// <summary>Returns true when <paramref name="context"/> carries all required fields and valid enum values.</summary>
    public static bool IsTrusted(CapabilityCaller context) => RuntimeCallerContext.IsTrusted(context);
}
