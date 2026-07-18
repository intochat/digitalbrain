namespace TripRadar.Server.Comms.Core.Helpers;

public static class StringHelper
{
    /// <summary>
    /// Masks an email address for logging purposes to prevent PII leakage.
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "[no-email]";
        }

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
        {
            return "***";
        }

        var localPart = email[..atIndex];
        var domain = email[atIndex..];
        var maskedLocal = localPart.Length > 1 ? $"{localPart[0]}***" : "***";
        return $"{maskedLocal}{domain}";
    }
}