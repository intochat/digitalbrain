using DigitalBrain.Runtime.Neurons;
using Orleans;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record TranslateTextRequest(
    [property: Id(0)] string Text,
    [property: Id(1)] string TargetLanguage) : Synapse;

[GenerateSerializer]
public sealed record TextTranslatedEvent(
    [property: Id(0)] string OriginalText,
    [property: Id(1)] string TranslatedText,
    [property: Id(2)] string TargetLanguage) : Synapse;

[GenerateSerializer]
public sealed record SystemAlertFiredEvent(
    [property: Id(0)] string Severity, // "Info", "Warning", "Critical"
    [property: Id(1)] string AlertSummary) : Synapse;
