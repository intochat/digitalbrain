using System.Text.RegularExpressions;

namespace DigitalBrain.Kernel.Ino;

// Prompt-sniffing helpers shared by InoNeuron's Gmail/Salesforce connector dispatch.
public static class InoConnectorIntents
{
    public static bool IsGmail(string prompt) => GmailIntentRegex().IsMatch(prompt);

    public static bool IsSalesforce(string prompt) => SalesforceIntentRegex().IsMatch(prompt);

    public static int ResultCount(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        return p.Contains("last") || p.Contains("latest") || p.Contains("most recent") ? 1 : 5;
    }

    private static Regex GmailIntentRegex() =>
        new(@"\b(gmail|email|e-mail|mailbox|inbox)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex SalesforceIntentRegex() =>
        new(@"\b(salesforce|crm)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
