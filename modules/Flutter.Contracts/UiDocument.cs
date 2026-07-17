using System.Text.Json;
using System.Text.Json.Serialization;
using Brain.Contracts;

namespace Flutter.Contracts;

public sealed record UiDocument(int Version, IReadOnlyList<UiBlock> Blocks)
{
    public const int CurrentVersion = 1;
    public const int MaximumTextLength = 16_384;
    public const int MaximumNestingDepth = 8;
    public const int MaximumDocumentBytes = 262_144;

    private static readonly HashSet<string> AllowedKinds =
        ["text", "heading", "list", "card", "button", "status"];

    public static UiDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw Invalid("document is required");
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
            throw Invalid($"document exceeds {MaximumDocumentBytes} bytes");

        UiDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<UiDocument>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            throw Invalid("malformed UI document JSON");
        }

        if (document is null || document.Version != CurrentVersion || document.Blocks is null)
            throw Invalid($"UI document version must be {CurrentVersion}");

        foreach (var block in document.Blocks)
            ValidateBlock(block, 1);

        return document;
    }

    private static void ValidateBlock(UiBlock? block, int depth)
    {
        if (block is null)
            throw Invalid("blocks cannot contain null entries");
        if (depth > MaximumNestingDepth)
            throw Invalid($"UI document nesting exceeds {MaximumNestingDepth}");
        if (string.IsNullOrWhiteSpace(block.Kind) || !AllowedKinds.Contains(block.Kind))
            throw Invalid($"unsupported UI block kind '{Truncate(block.Kind)}'");

        ValidateText(block.Text, nameof(block.Text));
        ValidateText(block.Label, nameof(block.Label));
        ValidateText(block.Value, nameof(block.Value));

        if (block.Action is not null)
            ValidateAction(block.Action);

        if (block.Kind == "button" && block.Action is null)
            throw Invalid("button requires an action");
        if (block.Action is not null && block.Kind != "button")
            throw Invalid("actions are allowed only on button blocks");

        if (block.Children is null)
            return;
        if (block.Kind is not ("list" or "card"))
            throw Invalid("children are allowed only on list and card blocks");

        foreach (var child in block.Children)
            ValidateBlock(child, depth + 1);
    }

    private static void ValidateAction(UiAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Contract) || action.Contract.Length > 256)
            throw Invalid("action contract is required and bounded");
        if (string.IsNullOrWhiteSpace(action.Target) || action.Target.Length > 512)
            throw Invalid("action target is required and bounded");
        if (string.IsNullOrWhiteSpace(action.InputJson) ||
            System.Text.Encoding.UTF8.GetByteCount(action.InputJson) > 32_768)
            throw Invalid("action inputJson is required and bounded");

        try
        {
            using var input = JsonDocument.Parse(action.InputJson);
            if (input.RootElement.ValueKind != JsonValueKind.Object)
                throw Invalid("action inputJson must contain a JSON object");
        }
        catch (JsonException)
        {
            throw Invalid("action inputJson is malformed");
        }
    }

    private static void ValidateText(string? value, string field)
    {
        if (value is not null && value.Length > MaximumTextLength)
            throw Invalid($"{field} exceeds {MaximumTextLength} characters");
    }

    private static BrainException Invalid(string detail) => new("input.invalid", detail);

    private static string Truncate(string? value) =>
        value is null ? string.Empty : value[..Math.Min(value.Length, 64)];
}

public sealed record UiBlock(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] UiAction? Action = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<UiBlock>? Children = null);

public sealed record UiAction(
    string Contract,
    string Target,
    string InputJson);
