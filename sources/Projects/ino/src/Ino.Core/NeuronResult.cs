using System.Diagnostics.CodeAnalysis;

namespace Ino.Core;

/// <summary>
/// Return type from INeuron&lt;T&gt;.HandleAsync. Carries success/failure, an optional
/// human-readable message, an optional typed error, an optional typed response payload
/// (for request/response synapse patterns), and an optional Remote Flutter Widget
/// description for neurons that render rich cards.
/// </summary>
/// <remarks>
/// <see cref="ResponsePayload"/> is stored as <see cref="ISynapse"/> so any concrete
/// payload type erases to the interface. Retrieve the typed payload via
/// <see cref="TryGetPayload{T}(out T)"/> — do not cast <c>ResponsePayload</c> directly.
/// </remarks>
[GenerateSerializer]
public sealed record NeuronResult
{
    /// <summary>
    /// Internal so external callers must go through the Ok/Fail factories. Orleans's
    /// generated serializer (in this assembly) can still invoke this for deserialization.
    /// External code that needs to derive a result from another result uses with-expressions
    /// (Ok().With(payload), result with { Rfw = bytes }) — those don't go through this ctor.
    /// </summary>
    internal NeuronResult(
        bool success,
        string? message = null,
        SynapseError? error = null,
        ISynapse? responsePayload = null,
        RfwPayload? rfw = null)
    {
        Success = success;
        Message = message;
        Error = error;
        ResponsePayload = responsePayload;
        Rfw = rfw;
    }

    [Id(0)] public bool Success { get; init; }
    [Id(1)] public string? Message { get; init; }
    [Id(2)] public SynapseError? Error { get; init; }
    [Id(3)] public ISynapse? ResponsePayload { get; init; }
    [Id(4)] public RfwPayload? Rfw { get; init; }

    public static NeuronResult Ok(string? message = null) => new(true, message);

    public static NeuronResult Fail(SynapseError error) => new(false, error.Message, error);

    public static NeuronResult Fail(SynapseErrorCode code, string message) =>
        Fail(new SynapseError(code, message));

    public NeuronResult With<T>(T payload) where T : ISynapse =>
        this with { ResponsePayload = payload };

    public NeuronResult WithRfwPayload(RfwPayload payload) =>
        this with { Rfw = payload };

    public bool TryGetPayload<T>([MaybeNullWhen(false)] out T? payload) where T : ISynapse
    {
        if (ResponsePayload is T typed)
        {
            payload = typed;
            return true;
        }
        payload = default;
        return false;
    }
}
