using DigitalBrain.Contracts.Types;
using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;
using Xunit;

namespace DigitalBrain.Modules.Supabase.Tests.Unit.Table;

// D6 reads: only Public columns reach the assistant. The classification is conservative by name
// (it may hide more than strictly necessary, never less) and typed through the P1.1 catalog.
public sealed class SupabaseTableReadPolicyFacts
{
    [Theory]
    [InlineData("email", FieldKind.Email, SensitivityClass.Personal)]
    [InlineData("work_email", FieldKind.Email, SensitivityClass.Personal)]
    [InlineData("api_key", FieldKind.Secret, SensitivityClass.Credential)]
    [InlineData("password_hash", FieldKind.Secret, SensitivityClass.Credential)]
    [InlineData("phone_number", FieldKind.PlainText, SensitivityClass.Personal)]
    [InlineData("customer_address", FieldKind.PlainText, SensitivityClass.Personal)]
    [InlineData("date_of_birth", FieldKind.PlainText, SensitivityClass.Personal)]
    [InlineData("iban", FieldKind.PlainText, SensitivityClass.Personal)]
    [InlineData("card_number", FieldKind.PlainText, SensitivityClass.Personal)]
    public void SensitiveNamesWithoutAPublicCatalogKindAreHidden(string name, FieldKind kind, SensitivityClass sensitivity)
    {
        var column = TextColumn(name);

        Assert.Equal(kind, SupabaseTableReadPolicy.KindOf(column));
        Assert.Equal(sensitivity, SupabaseTableReadPolicy.SensitivityOf(column));
        Assert.False(SupabaseTableReadPolicy.IsPublic(column));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("company")]
    [InlineData("city")]
    [InlineData("status")]
    [InlineData("active")]
    [InlineData("public")]
    [InlineData("primary_key")]
    [InlineData("shipping")]
    public void OrdinaryBusinessNamesStayPublic(string name)
    {
        var column = TextColumn(name);

        Assert.Equal(FieldKind.PlainText, SupabaseTableReadPolicy.KindOf(column));
        Assert.Equal(SensitivityClass.Public, SupabaseTableReadPolicy.SensitivityOf(column));
        Assert.True(SupabaseTableReadPolicy.IsPublic(column));
    }

    [Fact]
    public void NonTextColumnsKeepTheirNumberKindAndPublicSensitivity()
    {
        var column = new SupabaseTableColumn("amount", "amount", SupabaseTypeMap.Number);

        Assert.Equal(FieldKind.Number, SupabaseTableReadPolicy.KindOf(column));
        Assert.Equal(SensitivityClass.Public, SupabaseTableReadPolicy.SensitivityOf(column));
    }

    [Fact]
    public void PublicColumnsProjectOnlyReadableColumns()
    {
        SupabaseTableSnapshot snapshot = new("t", "T", 1,
            [TextColumn("id"), TextColumn("company"), TextColumn("email"), TextColumn("api_key")],
            [], [], null, ["id", "company", "email", "api_key"], 0, 0, 0, 25);

        var readable = SupabaseTableReadPolicy.PublicColumns(snapshot);

        Assert.Equal(["id", "company"], readable.Select(column => column.Id));
    }

    private static SupabaseTableColumn TextColumn(string name) => new(name, name, SupabaseTypeMap.Text);
}