using System.Text.RegularExpressions;
using DigitalBrain.Modules.Configuration;

namespace DigitalBrain.Capabilities.Connections.Connectors;

// The Integration Neuron reads its configured fields from host configuration under
// "<Root>:Integrations:<id>:<field>". Every integration uses the same key layout.
public sealed record IntegrationSchema(string Id, string DisplayName, string[] SecretFields, string[] SettingFields)
{
    private static readonly Regex SafeId = new("^[a-z][a-z0-9.-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex SafeField = new("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string SectionName => ModuleConfiguration.IntegrationsSectionName;

    public static string Key(string id, string field) => ConfigurationKeyGrammar.IntegrationKey(id, field);

    public string Key(string field) => Key(Id, field);

    public static IntegrationSchema For(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (id is null || !SafeId.IsMatch(id))
        {
            throw new ArgumentException("An integration id is lowercase letters, digits, dots and dashes.");
        }

        return new IntegrationSchema(id, displayName, [], []);
    }

    public IntegrationSchema RequiresSecret(string field) => this with { SecretFields = [.. SecretFields, Checked(field)] };

    public IntegrationSchema RequiresSetting(string field) => this with { SettingFields = [.. SettingFields, Checked(field)] };

    public string[] OptionalSettingFields { get; init; } = [];
    public string[] OptionalSecretFields { get; init; } = [];

    public IntegrationSchema OffersSecret(string field) => this with { OptionalSecretFields = [.. OptionalSecretFields, Checked(field)] };

    public IntegrationSchema OffersSetting(string field) => this with { OptionalSettingFields = [.. OptionalSettingFields, Checked(field)] };

    public IReadOnlyList<string> RequiredFields => [.. SecretFields, .. SettingFields];

    public IReadOnlyList<string> AllFields => [.. RequiredFields, .. OptionalSettingFields, .. OptionalSecretFields];

    public bool IsSetting(string field) => SettingFields.Contains(field, StringComparer.Ordinal) || OptionalSettingFields.Contains(field, StringComparer.Ordinal);

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
