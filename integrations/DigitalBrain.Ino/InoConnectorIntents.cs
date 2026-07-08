namespace DigitalBrain.Ino;

public static class InoConnectorIntents
{
    public static bool IsGmail(string prompt) =>
        InoIntentClassifier.Classify(prompt).Intent == "gmail";

    public static bool IsSalesforce(string prompt) =>
        InoIntentClassifier.Classify(prompt).Intent == "salesforce";

    public static int ResultCount(string prompt) => InoPromptSemantics.ResultCount(prompt) ?? 5;
}
