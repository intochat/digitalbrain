using System.Text;
using System.Text.Json;
using Brain.Contracts;

namespace Brain.Modules.Workspace;

public sealed record Block(string Kind, string Json);

public sealed record BlockAction(string Label, string Contract, string InputJson);

public sealed record BlockDoc(string Json)
{
    public const int MaxBytes = 65536;
    public const int MaxDepth = 8;

    private static readonly string[] KnownKinds =
    [
        "section", "columns", "text", "metric", "field", "list",
        "table", "timeline", "entry", "media", "progress", "actionRow"
    ];

    public static BlockDoc Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
            throw new BrainException("input.invalid", "document exceeds maximum size");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("version", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var version) ||
                version != 1)
                throw new BrainException("input.invalid", "version must be 1");

            if (!root.TryGetProperty("blocks", out var blocksElement) ||
                blocksElement.ValueKind != JsonValueKind.Array)
                throw new BrainException("input.invalid", "blocks must be an array");

            foreach (var block in blocksElement.EnumerateArray())
                ValidateBlock(block, 1);
        }

        return new BlockDoc(json);
    }

    private static void ValidateBlock(JsonElement block, int depth)
    {
        if (depth > MaxDepth)
            throw new BrainException("input.invalid", "block nesting exceeds maximum depth");

        if (block.ValueKind != JsonValueKind.Object ||
            !block.TryGetProperty("kind", out var kindElement) ||
            kindElement.ValueKind != JsonValueKind.String)
            throw new BrainException("input.invalid", "block is missing a kind");

        var kind = kindElement.GetString();
        if (kind is null || Array.IndexOf(KnownKinds, kind) < 0)
        {
            var truncatedKind = kind is null ? "" : kind[..Math.Min(kind.Length, 64)];
            throw new BrainException("input.invalid", $"unknown block kind '{truncatedKind}'");
        }

        if (block.TryGetProperty("children", out var childrenElement))
        {
            if (childrenElement.ValueKind != JsonValueKind.Array)
                throw new BrainException("input.invalid", "children must be an array");
            foreach (var child in childrenElement.EnumerateArray())
                ValidateBlock(child, depth + 1);
        }

        if (block.TryGetProperty("entries", out var entriesElement))
        {
            if (entriesElement.ValueKind != JsonValueKind.Array)
                throw new BrainException("input.invalid", "entries must be an array");
            foreach (var entry in entriesElement.EnumerateArray())
                ValidateBlock(entry, depth + 1);
        }

        if (kind == "actionRow")
        {
            if (!block.TryGetProperty("actions", out var actionsElement) ||
                actionsElement.ValueKind != JsonValueKind.Array)
                throw new BrainException("input.invalid", "actions must be an array");

            foreach (var action in actionsElement.EnumerateArray())
                ValidateAction(action, depth + 1);
        }
    }

    private static readonly string[] ActionFields = ["label", "contract", "inputJson"];

    private static void ValidateAction(JsonElement action, int depth)
    {
        if (depth > MaxDepth)
            throw new BrainException("input.invalid", "block nesting exceeds maximum depth");

        if (action.ValueKind != JsonValueKind.Object)
            throw new BrainException("input.invalid", "action must be an object");

        var matchedFields = new bool[ActionFields.Length];
        foreach (var property in action.EnumerateObject())
        {
            var fieldIndex = Array.IndexOf(ActionFields, property.Name);
            if (fieldIndex < 0)
                throw new BrainException("input.invalid", "action has an unexpected property");
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new BrainException("input.invalid", "action fields must be strings");
            if (matchedFields[fieldIndex])
                throw new BrainException("input.invalid", "action has a duplicate property");
            matchedFields[fieldIndex] = true;
        }

        if (Array.IndexOf(matchedFields, false) >= 0)
            throw new BrainException("input.invalid", "action must have label, contract, and inputJson");
    }
}

public static class Blocks
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static BlockDoc Doc(params Block[] blocks) =>
        new($$"""{"version":1,"blocks":[{{RawJoin(blocks)}}]}""");

    public static Block Section(string title, params Block[] children) =>
        new("section", $$"""{"kind":"section","title":{{Str(title)}},"children":[{{RawJoin(children)}}]}""");

    public static Block Columns(params Block[] children) =>
        new("columns", $$"""{"kind":"columns","children":[{{RawJoin(children)}}]}""");

    public static Block Text(string value) =>
        new("text", $$"""{"kind":"text","value":{{Str(value)}}}""");

    public static Block Metric(string label, object value) =>
        new("metric", $$"""{"kind":"metric","label":{{Str(label)}},"value":{{JsonSerializer.Serialize(value, SerializerOptions)}}}""");

    public static Block Field(string label, string value) =>
        new("field", $$"""{"kind":"field","label":{{Str(label)}},"value":{{Str(value)}}}""");

    public static Block List(params string[] items) =>
        new("list", $$"""{"kind":"list","items":{{JsonSerializer.Serialize(items, SerializerOptions)}}}""");

    public static Block Table(string[] columns, params string[][] rows) =>
        new("table", $$"""{"kind":"table","columns":{{JsonSerializer.Serialize(columns, SerializerOptions)}},"rows":{{JsonSerializer.Serialize(rows, SerializerOptions)}}}""");

    public static Block Timeline(IEnumerable<Block> entries) =>
        new("timeline", $$"""{"kind":"timeline","entries":[{{RawJoin(entries)}}]}""");

    public static Block Entry(string title, string detail) =>
        new("entry", $$"""{"kind":"entry","title":{{Str(title)}},"detail":{{Str(detail)}}}""");

    public static Block Media(string url, string alt) =>
        new("media", $$"""{"kind":"media","url":{{Str(url)}},"alt":{{Str(alt)}}}""");

    public static Block Progress(string label, double fraction)
    {
        if (!double.IsFinite(fraction))
            throw new BrainException("input.invalid", "fraction must be finite");

        return new("progress", $$"""{"kind":"progress","label":{{Str(label)}},"fraction":{{JsonSerializer.Serialize(fraction)}}}""");
    }

    public static Block ActionRow(params BlockAction[] actions) =>
        new("actionRow", $$"""{"kind":"actionRow","actions":[{{string.Join(",", actions.Select(ActionJson))}}]}""");

    public static BlockAction Action(string label, string contract, string inputJson) =>
        new(label, contract, inputJson);

    private static string ActionJson(BlockAction action) =>
        $$"""{"label":{{Str(action.Label)}},"contract":{{Str(action.Contract)}},"inputJson":{{Str(action.InputJson)}}}""";

    private static string RawJoin(IEnumerable<Block> blocks) =>
        string.Join(",", blocks.Select(b => b.Json));

    private static string Str(string value) => JsonSerializer.Serialize(value);
}
