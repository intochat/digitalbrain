using DigitalBrain.Contracts.Types;
using DigitalBrain.Supabase.Tables;

namespace DigitalBrain.Supabase;

// D6: the assistant sees schema, counts and aggregates, and row values only for Public columns
// (or under a per-connection grant, which does not exist yet). Columns are typed through the P1.1
// catalog; text columns whose names denote well-known personal or credential material raise the
// sensitivity of the catalog kind, so nothing sensitive is ever projected to the model. The
// classification only ever hides more, never less.
public static class SupabaseTableReadPolicy
{
    private static readonly string[] CredentialMarkers =
        ["password", "passwd", "secret", "token", "api_key", "apikey", "credential", "private_key", "access_key", "bearer", "session", "signature", "salt", "hash"];

    private static readonly string[] PersonalMarkers =
        ["first_name", "last_name", "full_name", "surname", "given_name", "family_name", "display_name",
         "date_of_birth", "birthdate", "birth_date", "dob", "ssn", "social_security", "national_id", "tax_id",
         "passport", "phone", "mobile", "address", "street", "postcode", "postal", "zip",
         "iban", "bank_account", "routing", "card_number", "cvv", "salary"];

    public static FieldKind KindOf(SupabaseTableColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        var name = column.Id.ToLowerInvariant();
        if (CredentialMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal))) { return FieldKind.Secret; }
        if (name.Contains("email", StringComparison.Ordinal) || name.Contains("e_mail", StringComparison.Ordinal)) { return FieldKind.Email; }
        return SupabaseTypeMap.ToFieldKind(column.Type);
    }

    public static SensitivityClass SensitivityOf(SupabaseTableColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        var byKind = TypeCatalog.Get(KindOf(column)).Sensitivity;
        var name = column.Id.ToLowerInvariant();
        if (CredentialMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal))) { return SensitivityClass.Credential; }
        if (PersonalMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal))) { return SensitivityClass.Personal; }
        return byKind;
    }

    public static bool IsPublic(SupabaseTableColumn column) => SensitivityOf(column) == SensitivityClass.Public;

    public static IReadOnlyList<SupabaseTableColumn> PublicColumns(SupabaseTableSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Columns.Where(IsPublic).ToArray();
    }
}