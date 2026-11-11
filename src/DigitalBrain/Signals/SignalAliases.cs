using System.Reflection;
using System.Text.Json;

namespace DigitalBrain.Kernel;

/// <summary>
/// A journaled signal whose payload type no loaded unit provides: a runtime hands it to alias reactions with the journal's raw JSON.
/// Typed reactions never see it.
/// </summary>
internal interface IUntypedSignal
{
    /// <summary>The journal alias.</summary>
    string Alias { get; }

    /// <summary>The payload exactly as the journal holds it.</summary>
    JsonElement Payload { get; }
}

/// <summary>The journal alias of a signal: its <c>[Signal]</c> alias, else its type name.</summary>
internal static class SignalAliases
{
    public static string Of(Type signal) => signal.GetCustomAttribute<SignalAttribute>(false)?.Alias ?? signal.Name;

    public static string Of(Signal signal) => signal is IUntypedSignal untyped ? untyped.Alias : Of(signal.GetType());
}
