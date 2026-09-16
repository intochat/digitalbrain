using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Runtime;

namespace DigitalBrain.Core;

internal static class CallerContext
{
    internal static NeuronId Current() =>
        RequestContext.Get(NeuronRequestKeys.Caller) is string text && NeuronId.TryParse(text, out var caller)
            ? caller : NeuronId.Plain("anonymous");

    internal static CorrelationId? CurrentCorrelation() =>
        RequestContext.Get(NeuronRequestKeys.Correlation) is string text && Guid.TryParseExact(text, "N", out var id) && id != Guid.Empty
            ? new CorrelationId(id) : null;

    internal static SignalId? CurrentCausation() =>
        RequestContext.Get(NeuronRequestKeys.Causation) is string text && Guid.TryParseExact(text, "N", out var id) && id != Guid.Empty
            ? new SignalId(id) : null;
}
