namespace DigitalBrain.Conversations;

public static class ConversationIdentity
{
    private const char OwnerNameSeparator = '/';

    public static string Validated(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var trimmed = value.Trim();
        if (trimmed.Contains(OwnerNameSeparator, StringComparison.Ordinal) || trimmed.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                $"A conversation name cannot contain '{OwnerNameSeparator}' or whitespace; "
                + $"'{value}' is not addressable.",
                parameterName);
        }

        return trimmed;
    }
}
