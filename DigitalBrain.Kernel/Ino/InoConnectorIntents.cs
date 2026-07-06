namespace DigitalBrain.Kernel.Ino;

// Prompt classification helpers shared by InoNeuron.
// Regex removed in favor of InoIntentClassifier (keyword fast-path + LLM structured path).
// Legacy Is* kept for minimal diff; new code should prefer InoIntentClassifier.Classify / ClassifyWithLlmAsync.
public static class InoConnectorIntents
{
    public static bool IsGmail(string prompt) =>
        InoIntentClassifier.Classify(prompt).Intent == "gmail";

    public static bool IsSalesforce(string prompt) =>
        InoIntentClassifier.Classify(prompt).Intent == "salesforce";

    public static int ResultCount(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        return p.Contains("last") || p.Contains("latest") || p.Contains("most recent") ? 1 : 5;
    }
}
