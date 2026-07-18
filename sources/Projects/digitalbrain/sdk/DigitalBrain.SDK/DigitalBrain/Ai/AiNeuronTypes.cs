namespace DigitalBrain.SDK.DigitalBrain.Ai;

// Wire-level type names for AI neurons that other silos (notably the kernel)
// need to address as ReceiverNeuronType. Kept in Contracts so dependents
// don't have to reference the AI silo just to spell the receiver's name.
public static class AiNeuronTypes
{
    public const string IntentNeuron   = nameof(IntentNeuron);
    public const string PlannerNeuron  = nameof(PlannerNeuron);
    public const string ExplainerNeuron = nameof(ExplainerNeuron);
    public const string BrainstormNeuron = nameof(BrainstormNeuron);
    public const string LlmTranslationNeuron = nameof(LlmTranslationNeuron);
    public const string LlmAlertingNeuron = nameof(LlmAlertingNeuron);
    public const string DocumentParserNeuron = nameof(DocumentParserNeuron);
    public const string VisualCanvasNeuron = nameof(VisualCanvasNeuron);
}
