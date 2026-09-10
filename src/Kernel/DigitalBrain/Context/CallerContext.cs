using DigitalBrain.Abstractions.Identity;
using Orleans.Runtime;

namespace DigitalBrain.Core;

internal static class CallerContext
{
    internal const string Caller = "db.caller";
    internal const string Correlation = "db.correlation";
    internal const string Causation = "db.causation";

    internal static NeuronId Current() =>
        RequestContext.Get(Caller) is string text && NeuronId.TryParse(text, out var caller)
            ? caller : NeuronId.Plain("anonymous");

    internal static CorrelationId? CurrentCorrelation() =>
        RequestContext.Get(Correlation) is string text && Guid.TryParseExact(text, "N", out var id) && id != Guid.Empty
            ? new CorrelationId(id) : null;

    internal static SignalId? CurrentCausation() =>
        RequestContext.Get(Causation) is string text && Guid.TryParseExact(text, "N", out var id) && id != Guid.Empty
            ? new SignalId(id) : null;
}
