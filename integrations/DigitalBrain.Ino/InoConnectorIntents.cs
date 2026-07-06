namespace DigitalBrain.Ino;

// Prompt helpers for Ino connectors.
// Delegates to the classifier (owned by Ino integration).
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
