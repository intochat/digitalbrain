using System.Text.RegularExpressions;

namespace DigitalBrain.Sdk.Integrations;

public sealed record IntegrationDefinition(string Id, string DisplayName, string[] SecretFields, string[] SettingFields)
{
    private static readonly Regex SafeId = new("^[a-z][a-z0-9.-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex SafeField = new("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static IntegrationDefinition For(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (id is null || !SafeId.IsMatch(id))
        {
            throw new ArgumentException("An integration id is lowercase letters, digits, dots and dashes.");
        }

        return new IntegrationDefinition(id, displayName, [], []);
    }

    public IntegrationDefinition RequiresSecret(string field) => this with { SecretFields = [.. SecretFields, Checked(field)] };

    public IntegrationDefinition RequiresSetting(string field) => this with { SettingFields = [.. SettingFields, Checked(field)] };

    // Secrets first, then settings; the order missing fields are reported in.
    public IReadOnlyList<string> AllFields => [.. SecretFields, .. SettingFields];

    private string Checked(string field)
    {
        if (field is null || !SafeField.IsMatch(field))
        {
            throw new ArgumentException("A field name is letters and digits, starting with a letter.");
        }

        if (AllFields.Contains(field, StringComparer.Ordinal))
        {
            throw new ArgumentException("A field is declared once per integration.");
        }

        return field;
    }
}
