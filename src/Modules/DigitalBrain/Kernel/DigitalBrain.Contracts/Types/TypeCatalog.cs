using System.Globalization;
using System.Text.Json.Nodes;

namespace DigitalBrain.Contracts.Types;

public static class TypeCatalog
{
    private static readonly SemanticType[] Catalog = BuildCatalog();

    private static readonly IReadOnlyDictionary<FieldKind, SemanticType> ByKind =
        Catalog.ToDictionary(type => type.Kind);

    public static IReadOnlyList<SemanticType> All => Catalog;

    public static IReadOnlyList<FieldKind> McpFormKinds { get; } =
        [.. Catalog.Where(type => type.ToMcpFormPrimitive() is not null).Select(type => type.Kind)];

    public static SemanticType Get(FieldKind kind) =>
        ByKind.TryGetValue(kind, out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No semantic type is registered for this field kind.");

    public static bool IsMcpFormMappable(FieldKind kind) => McpFormKinds.Contains(kind);

    public static IReadOnlyList<string> AllowedFor(FieldKind kind) => kind switch
    {
        FieldKind.PlainText => ["text"],
        FieldKind.LongText => ["long text"],
        FieldKind.Number => ["a finite number"],
        FieldKind.Date => ["an ISO 8601 date (yyyy-MM-dd)"],
        FieldKind.DateTime => ["an ISO 8601 date-time"],
        FieldKind.Boolean => ["true", "false"],
        FieldKind.Choice => ["one of the declared choices"],
        FieldKind.MultiChoice => ["one or more of the declared choices"],
        FieldKind.Email => ["an email address"],
        FieldKind.Url => ["an absolute http(s) URL"],
        FieldKind.Secret => ["a SecretRef handle; raw secret values are never accepted"],
        FieldKind.Reference => ["an entity reference or id"],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No semantic type is registered for this field kind."),
    };

    public static object Validate(object? value, FieldKind kind) => kind switch
    {
        FieldKind.PlainText or FieldKind.LongText or FieldKind.Choice => ValidateText(kind, value),
        FieldKind.Number => ValidateNumber(value),
        FieldKind.Date => ValidateDate(value),
        FieldKind.DateTime => ValidateDateTime(value),
        FieldKind.Boolean => ValidateBoolean(value),
        FieldKind.MultiChoice => ValidateMultiChoice(value),
        FieldKind.Email => ValidateEmail(value),
        FieldKind.Url => ValidateUrl(value),
        FieldKind.Secret => ValidateSecret(value),
        FieldKind.Reference => ValidateReference(value),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No semantic type is registered for this field kind."),
    };

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

    private static SemanticType[] BuildCatalog() =>
    [
        new(FieldKind.PlainText, "plain-text", PrimitiveKind.String, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.TextBox, DisplayWidgetKind.Text, RedactorKind.Erase, true),
        new(FieldKind.LongText, "long-text", PrimitiveKind.String, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.MultilineTextBox, DisplayWidgetKind.LongText, RedactorKind.Erase, true),
        new(FieldKind.Number, "number", PrimitiveKind.Number, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.NumberBox, DisplayWidgetKind.Number, RedactorKind.Erase, true),
        new(FieldKind.Date, "date", PrimitiveKind.Date, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.DatePicker, DisplayWidgetKind.Date, RedactorKind.Erase, true),
        new(FieldKind.DateTime, "date-time", PrimitiveKind.DateTime, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.DateTimePicker, DisplayWidgetKind.DateTime, RedactorKind.Erase, true),
        new(FieldKind.Boolean, "boolean", PrimitiveKind.Boolean, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.Checkbox, DisplayWidgetKind.Boolean, RedactorKind.Erase, true),
        new(FieldKind.Choice, "choice", PrimitiveKind.String, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.Select, DisplayWidgetKind.Choice, RedactorKind.Erase, true),
        new(FieldKind.MultiChoice, "multi-choice", PrimitiveKind.Array, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.MultiSelect, DisplayWidgetKind.MultiChoice, RedactorKind.Erase, true),
        new(FieldKind.Email, "email", PrimitiveKind.String, SensitivityClass.Personal, LlmExposure.Value, InputWidgetKind.EmailBox, DisplayWidgetKind.Email, RedactorKind.Mask, false),
        new(FieldKind.Url, "url", PrimitiveKind.String, SensitivityClass.Public, LlmExposure.Value, InputWidgetKind.UrlBox, DisplayWidgetKind.Link, RedactorKind.Erase, true),
        new(FieldKind.Secret, "secret", PrimitiveKind.Reference, SensitivityClass.Credential, LlmExposure.ReferenceOnly, InputWidgetKind.OutOfBand, DisplayWidgetKind.Masked, RedactorKind.Mask, false),
        new(FieldKind.Reference, "reference", PrimitiveKind.Reference, SensitivityClass.Public, LlmExposure.ReferenceOnly, InputWidgetKind.ReferencePicker, DisplayWidgetKind.Reference, RedactorKind.Erase, false),
    ];

    private static string ValidateText(FieldKind kind, object? value) =>
        value is string text ? text : throw new TypeValidationException(kind, value);

    private static decimal ValidateNumber(object? value) => value switch
    {
        decimal number => number,
        int integer => integer,
        long longInteger => longInteger,
        double doubleNumber when double.IsFinite(doubleNumber) => (decimal)doubleNumber,
        string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => throw new TypeValidationException(FieldKind.Number, value),
    };

    private static DateOnly ValidateDate(object? value) => value switch
    {
        DateOnly date => date,
        DateTime dateTime => DateOnly.FromDateTime(dateTime),
        string text when DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
        _ => throw new TypeValidationException(FieldKind.Date, value),
    };

    private static DateTime ValidateDateTime(object? value) => value switch
    {
        DateTime dateTime => dateTime,
        DateTimeOffset offset => offset.DateTime,
        string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) => parsed,
        _ => throw new TypeValidationException(FieldKind.DateTime, value),
    };

    private static bool ValidateBoolean(object? value) => value switch
    {
        bool boolean => boolean,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => throw new TypeValidationException(FieldKind.Boolean, value),
    };

    private static string[] ValidateMultiChoice(object? value) => value switch
    {
        string[] array => array,
        IEnumerable<string> sequence => [.. sequence],
        _ => throw new TypeValidationException(FieldKind.MultiChoice, value),
    };

    private static string ValidateEmail(object? value) =>
        value is string text && LooksLikeEmail(text) ? text : throw new TypeValidationException(FieldKind.Email, value);

    private static string ValidateUrl(object? value) =>
        value is string text && IsHttpUrl(text) ? text : throw new TypeValidationException(FieldKind.Url, value);

    private static SecretRef ValidateSecret(object? value) =>
        value is SecretRef secret && SecretRef.IsReference(secret.Reference)
            ? secret
            : throw new TypeValidationException(FieldKind.Secret, value);

    private static Reference ValidateReference(object? value) => value switch
    {
        Reference reference => reference,
        string entityId when !string.IsNullOrWhiteSpace(entityId) => new Reference(entityId),
        _ => throw new TypeValidationException(FieldKind.Reference, value),
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