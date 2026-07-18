using DigitalBrain.SDK.DigitalBrain.Ai.Explaining;
using DigitalBrain.SDK.DigitalBrain.Ai.NemoChat;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

public static class AiSynapseTypes
{
    public static readonly string LlmRequest = typeof(LlmRequest).FullName!;
    public static readonly string LlmResponse = typeof(LlmResponse).FullName!;
    public static readonly string Voice2TextRequest = typeof(Voice2TextRequest).FullName!;
    public static readonly string Voice2TextResponse = typeof(Voice2TextResponse).FullName!;
    public static readonly string EmbeddingRequest = typeof(EmbeddingRequest).FullName!;
    public static readonly string EmbeddingResponse = typeof(EmbeddingResponse).FullName!;
    public static readonly string ClassifyIntentRequest = typeof(ClassifyIntentRequest).FullName!;
    public static readonly string IntentClassified = typeof(IntentClassified).FullName!;
    public static readonly string ExplainerRequest  = typeof(ExplainerRequest).FullName!;
    public static readonly string ExplainerResponse = typeof(ExplainerResponse).FullName!;
    public static readonly string BrainstormRequest = typeof(BrainstormRequest).FullName!;
    public static readonly string BrainstormOptions = typeof(BrainstormOptions).FullName!;
    public static readonly string ChooseDirectionRequest = typeof(ChooseDirectionRequest).FullName!;
    public static readonly string NemoChatRequest = typeof(NemoChatRequest).FullName!;
    public static readonly string TranslateTextRequest = typeof(global::DigitalBrain.SDK.DigitalBrain.Ai.TranslateTextRequest).FullName!;
    public static readonly string TextTranslatedEvent = typeof(global::DigitalBrain.SDK.DigitalBrain.Ai.TextTranslatedEvent).FullName!;
    public static readonly string SystemAlertFiredEvent = typeof(global::DigitalBrain.SDK.DigitalBrain.Ai.SystemAlertFiredEvent).FullName!;
    public static readonly string ParseDocRequest = typeof(global::DigitalBrain.SDK.DigitalBrain.Ai.ParseDocRequest).FullName!;
    public static readonly string ConceptsExtractedEvent = typeof(global::DigitalBrain.SDK.DigitalBrain.Ai.ConceptsExtractedEvent).FullName!;
    public static readonly string CanvasRenderEvent = typeof(global::DigitalBrain.SDK.DigitalBrain.Ai.CanvasRenderEvent).FullName!;
}

