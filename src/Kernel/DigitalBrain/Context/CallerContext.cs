using DigitalBrain.Abstractions.Identity;
using Orleans.Runtime;

namespace DigitalBrain.Core;

// The outgoing call filter that stamps this key is Task A4; until then the edge sets it.
internal static class CallerContext
{
    internal const string Key = "db.caller";

    internal static NeuronId Current() =>
        RequestContext.Get(Key) is string text && NeuronId.TryParse(text, out var caller)
            ? caller : NeuronId.Plain("anonymous");
}
