using System.Globalization;
using System.Text.Json.Nodes;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Sdk.Types;

namespace DigitalBrain.Sdk.Types;

public static class TypeCatalog
{
    private sealed record Definition(SemanticType Type, string[] Allowed, Func<object?, object> Validate);
    private static readonly IReadOnlyDictionary<FieldKind, Definition> Definitions = BuildDefinitions().ToDictionary(d => d.Type.Kind);
    private static readonly SemanticType[] Catalog = Definitions.Values.Select(d => d.Type).ToArray();

    private static readonly IReadOnlyDictionary<FieldKind, SemanticType> ByKind =
        Catalog.ToDictionary(type => type.Kind);

    public static IReadOnlyList<SemanticType> All => Catalog;

    public static IReadOnlyList<FieldKind> McpFormKinds { get; } =
        [.. Catalog.Where(type => type.ToMcpFormPrimitive() is not null).Select(type => type.Kind)];

    public static SemanticType Get(FieldKind kind) =>
        ByKind.TryGetValue(kind, out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No semantic type is registered for this field kind.");

    public static IReadOnlyList<string> AllowedFor(FieldKind kind) => DefinitionFor(kind).Allowed;
    public static object Validate(object? value, FieldKind kind) => DefinitionFor(kind).Validate(value);

    private static Definition DefinitionFor(FieldKind kind) => Definitions.TryGetValue(kind, out var definition)
        ? definition : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No semantic type is registered for this field kind.");

    public static JsonObject ToJson()
    {
        var types = new JsonArray();
        foreach (var type in Catalog)
        {
            types.Add(new JsonObject
            {
                ["id"] = type.Id,
                ["kind"] = type.Kind.ToString(),
                ["primitive"] = type.Primitive.ToString(),
                ["sensitivity"] = type.Sensitivity.ToString(),
                ["llmExposure"] = type.LlmExposure.ToString(),
                ["inputWidget"] = type.InputWidget.ToString(),
                ["displayWidget"] = type.DisplayWidget.ToString(),
                ["redactor"] = type.Redactor.ToString(),
                ["indexable"] = type.Indexable,
            });
        }

        return new JsonObject
        {
            ["version"] = "v0",
            ["closed"] = true,
            ["types"] = types,
        };
    }

    private static Definition[] BuildDefinitions() =>
    [
        new(new(FieldKind.PlainText, "plain-text", PrimitiveKind.String, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.TextBox, DisplayWidgetKind.Text, RedactorKind.Erase, true), ["text"], v => ValidateText(FieldKind.PlainText, v)),
        new(new(FieldKind.LongText, "long-text", PrimitiveKind.String, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.MultilineTextBox, DisplayWidgetKind.LongText, RedactorKind.Erase, true), ["long text"], v => ValidateText(FieldKind.LongText, v)),
        new(new(FieldKind.Number, "number", PrimitiveKind.Number, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.NumberBox, DisplayWidgetKind.Number, RedactorKind.Erase, true), ["a finite number"], v => ValidateNumber(v)),
        new(new(FieldKind.Date, "date", PrimitiveKind.Date, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.DatePicker, DisplayWidgetKind.Date, RedactorKind.Erase, true), ["an ISO 8601 date (yyyy-MM-dd)"], v => ValidateDate(v)),
        new(new(FieldKind.DateTime, "date-time", PrimitiveKind.DateTime, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.DateTimePicker, DisplayWidgetKind.DateTime, RedactorKind.Erase, true), ["an ISO 8601 date-time with an explicit UTC offset"], v => ValidateDateTime(v)),
        new(new(FieldKind.Boolean, "boolean", PrimitiveKind.Boolean, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.Checkbox, DisplayWidgetKind.Boolean, RedactorKind.Erase, true), ["true", "false"], v => ValidateBoolean(v)),
        new(new(FieldKind.Choice, "choice", PrimitiveKind.String, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.Select, DisplayWidgetKind.Choice, RedactorKind.Erase, true), ["one of the declared choices"], v => ValidateText(FieldKind.Choice, v)),
        new(new(FieldKind.MultiChoice, "multi-choice", PrimitiveKind.Array, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.MultiSelect, DisplayWidgetKind.MultiChoice, RedactorKind.Erase, true), ["one or more of the declared choices"], ValidateMultiChoice),
        new(new(FieldKind.Email, "email", PrimitiveKind.String, SensitivityClass.Personal, LlmExposure.Value, InputWidgetKind.EmailBox, DisplayWidgetKind.Email, RedactorKind.Mask, false), ["an email address"], ValidateEmail),
        new(new(FieldKind.Url, "url", PrimitiveKind.String, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.UrlBox, DisplayWidgetKind.Link, RedactorKind.Erase, true), ["an absolute http(s) URL"], ValidateUrl),
        new(new(FieldKind.Secret, "secret", PrimitiveKind.Reference, SensitivityClass.Credential, LlmExposure.ReferenceOnly, InputWidgetKind.OutOfBand, DisplayWidgetKind.Masked, RedactorKind.Mask, false), ["a SecretRef handle; raw secret values are never accepted"], ValidateSecret),
        new(new(FieldKind.Reference, "reference", PrimitiveKind.Reference, SensitivityClass.Public, LlmExposure.ReferenceOnly, InputWidgetKind.ReferencePicker, DisplayWidgetKind.Reference, RedactorKind.Erase, false), ["an entity reference or id"], v => ValidateReference(v)),
    ];

    private static string ValidateText(FieldKind kind, object? value) =>
        value is string text ? text : throw new TypeValidationException(kind, value, AllowedFor(kind));

    private static decimal ValidateNumber(object? value) => value switch
    {
        decimal number => number,
        int integer => integer,
        long longInteger => longInteger,
        double doubleNumber when double.IsFinite(doubleNumber) => (decimal)doubleNumber,
        string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => throw new TypeValidationException(FieldKind.Number, value, AllowedFor(FieldKind.Number)),
    };

    private static DateOnly ValidateDate(object? value) => value switch
    {
        DateOnly date => date,
        DateTime dateTime => DateOnly.FromDateTime(dateTime),
        string text when DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
        _ => throw new TypeValidationException(FieldKind.Date, value, AllowedFor(FieldKind.Date)),
    };

    private static DateTimeOffset ValidateDateTime(object? value) => value switch
    {
        DateTime dateTime when dateTime.Kind == DateTimeKind.Utc => new DateTimeOffset(dateTime),
        DateTimeOffset offset => offset,
        string text when HasOffset(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
        _ => throw new TypeValidationException(FieldKind.DateTime, value, AllowedFor(FieldKind.DateTime)),
    };

    // Local/unspecified values depend on the machine timezone and are rejected.
    private static bool HasOffset(string text) => text.EndsWith('Z')
        || (text.Length >= 6 && text[^3] == ':' && (text[^6] == '+' || text[^6] == '-'));

    private static bool ValidateBoolean(object? value) => value switch
    {
        bool boolean => boolean,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => throw new TypeValidationException(FieldKind.Boolean, value, AllowedFor(FieldKind.Boolean)),
    };

    private static string[] ValidateMultiChoice(object? value) => value switch
    {
        string[] array => array,
        IEnumerable<string> sequence => [.. sequence],
        _ => throw new TypeValidationException(FieldKind.MultiChoice, value, AllowedFor(FieldKind.MultiChoice)),
    };

    private static string ValidateEmail(object? value) =>
        value is string text && LooksLikeEmail(text) ? text : throw new TypeValidationException(FieldKind.Email, value, AllowedFor(FieldKind.Email));

    private static string ValidateUrl(object? value) =>
        value is string text && IsHttpUrl(text) ? text : throw new TypeValidationException(FieldKind.Url, value, AllowedFor(FieldKind.Url));

    private static SecretRef ValidateSecret(object? value) =>
        value is SecretRef secret && SecretReferences.IsReference(secret.Reference)
            ? secret
            : throw new TypeValidationException(FieldKind.Secret, value, AllowedFor(FieldKind.Secret));

    private static Reference ValidateReference(object? value) => value switch
    {
        Reference reference => reference,
        string entityId when !string.IsNullOrWhiteSpace(entityId) => new Reference(entityId),
        _ => throw new TypeValidationException(FieldKind.Reference, value, AllowedFor(FieldKind.Reference)),
    };

    private static bool LooksLikeEmail(string text)
    {
        var at = text.IndexOf('@', StringComparison.Ordinal);
        return at > 0
            && at == text.LastIndexOf('@')
            && at < text.Length - 1
            && !text.Any(char.IsWhiteSpace)
            && text.IndexOf('.', at + 1) > at + 1;
    }

    private static bool IsHttpUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
