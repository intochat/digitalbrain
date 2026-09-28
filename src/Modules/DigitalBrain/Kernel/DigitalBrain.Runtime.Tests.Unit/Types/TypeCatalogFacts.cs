using System.Text.Json.Nodes;
using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Tests;

public sealed class TypeCatalogFacts
{
    private static readonly FieldKind[] FlatSubset =
    [
        FieldKind.PlainText,
        FieldKind.Number,
        FieldKind.Boolean,
        FieldKind.Date,
        FieldKind.DateTime,
        FieldKind.Email,
        FieldKind.Url,
        FieldKind.Choice,
        FieldKind.MultiChoice,
    ];

    private static readonly string[] RequiredSchemaExtensions =
    [
        "x-intochat-id",
        "x-intochat-kind",
        "x-intochat-primitive",
        "x-intochat-sensitivity",
        "x-intochat-llm-exposure",
        "x-intochat-input-widget",
        "x-intochat-display-widget",
        "x-intochat-redactor",
        "x-intochat-indexable",
        "x-intochat-mcp-form",
    ];

    [Fact]
    public void FlatSubsetMapsOneToOneToMcpFormElicitation()
    {
        Assert.Equal(FlatSubset.OrderBy(kind => kind), TypeCatalog.McpFormKinds.OrderBy(kind => kind));

        foreach (var kind in FlatSubset)
        {
            var type = TypeCatalog.Get(kind);
            Assert.NotNull(type.ToMcpFormPrimitive());

            var schema = type.ToJsonSchema();
            Assert.True((bool)schema["x-intochat-mcp-form"]!, $"{kind} must be form-elicitable.");
            Assert.Contains(schema["type"]!.GetValue<string>(), new[] { "string", "number", "boolean", "array" });
            if (schema.TryGetPropertyValue("format", out var format))
            {
                Assert.Contains(format!.GetValue<string>(), new[] { "email", "uri", "date", "date-time" });
            }
        }

        var nonFlat = Enum.GetValues<FieldKind>().Except(FlatSubset);
        foreach (var kind in nonFlat)
        {
            var type = TypeCatalog.Get(kind);
            Assert.Null(type.ToMcpFormPrimitive());
            Assert.False((bool)type.ToJsonSchema()["x-intochat-mcp-form"]!, $"{kind} must not be form-elicitable.");
        }
    }

    [Fact]
    public void SecretRefIsNeverCollectedByAForm()
    {
        var secret = TypeCatalog.Get(FieldKind.Secret);
        Assert.Equal(LlmExposure.ReferenceOnly, secret.LlmExposure);
        Assert.Equal(InputWidgetKind.OutOfBand, secret.InputWidget);
        Assert.Equal(SensitivityClass.Credential, secret.Sensitivity);
        Assert.False(secret.Indexable);
        Assert.Null(secret.ToMcpFormPrimitive());
        Assert.False((bool)secret.ToJsonSchema()["x-intochat-mcp-form"]!);

        var rejected = Assert.Throws<TypeValidationException>(() => TypeCatalog.Validate("hunter2", FieldKind.Secret));
        Assert.DoesNotContain("hunter2", rejected.Message, StringComparison.Ordinal);
        Assert.Null(rejected.RejectedValue);

        var handle = SecretRef.For("owner", "salesforce-api-key", "Salesforce API key", isSet: true);
        Assert.Same(handle, TypeCatalog.Validate(handle, FieldKind.Secret));
    }

    [Fact]
    public void DateRejectsTheValueDateAndNamesDate()
    {
        var rejected = Assert.Throws<TypeValidationException>(() => TypeCatalog.Validate("date", FieldKind.Date));

        Assert.Equal(FieldKind.Date, rejected.Kind);
        Assert.Contains("date", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("Date", rejected.Message, StringComparison.Ordinal);

        Assert.Equal(new DateOnly(2026, 9, 23), TypeCatalog.Validate("2026-09-23", FieldKind.Date));
    }

    [Fact]
    public void CatalogIsClosedOverExactlyTheV0Kinds()
    {
        var kinds = Enum.GetValues<FieldKind>();
        Assert.Equal(kinds.Length, TypeCatalog.All.Count);
        Assert.Equal(kinds.OrderBy(kind => kind), TypeCatalog.All.Select(type => type.Kind).OrderBy(kind => kind));
        Assert.Equal(kinds.Length, TypeCatalog.All.Select(type => type.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryTypeSchemaCarriesTheXIntochatExtensions()
    {
        foreach (var type in TypeCatalog.All)
        {
            var schema = type.ToJsonSchema();
            foreach (var extension in RequiredSchemaExtensions)
            {
                Assert.True(schema.ContainsKey(extension), $"{type.Kind} schema is missing {extension}.");
            }
        }
    }

    [Fact]
    public void EmbeddedJsonCatalogMatchesTheCodeCatalog()
    {
        using var stream = typeof(TypeCatalog).Assembly
            .GetManifestResourceStream("DigitalBrain.Contracts.Types.TypeCatalog.v0.json");
        Assert.NotNull(stream);

        var embedded = JsonNode.Parse(stream);
        Assert.True(JsonNode.DeepEquals(embedded, TypeCatalog.ToJson()),
            "TypeCatalog.v0.json is out of sync with the code catalog; regenerate the JSON catalog.");
    }
}