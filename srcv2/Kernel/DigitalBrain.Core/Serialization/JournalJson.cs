using System.Text.Json.Serialization.Metadata;

namespace DigitalBrain.Core;

internal static class JournalJson
{
    internal static IJsonTypeInfoResolver TypeInfoResolver { get; } =
        new DefaultJsonTypeInfoResolver();
}
