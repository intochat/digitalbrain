namespace DigitalBrain.Core;

public static class DbSchemaGraphMapper
{
    public static CanvasGraphSpec ToGraphCanvasSpec(DbSchemaModel schema)
    {
        var nodes = schema.Tables
            .Select(table =>
            {
                var foreignKeyColumns = table.ForeignKeys
                    .SelectMany(fk => fk.Columns)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var fields = table.Columns
                    .OrderBy(column => column.Ordinal)
                    .Select(column => new CanvasGraphField(
                        column.Name,
                        column.StoreType,
                        BadgeFor(column, foreignKeyColumns.Contains(column.Name)),
                        column.DefaultValue is null ? null : "default " + column.DefaultValue,
                        Key: column.PrimaryKeyOrdinal > 0))
                    .ToArray();

                var details = new Dictionary<string, object?>
                {
                    ["kind"] = table.Kind,
                    ["indexes"] = table.Indexes.Select(index => new Dictionary<string, object?>
                    {
                        ["name"] = index.Name,
                        ["columns"] = index.Columns,
                        ["unique"] = index.IsUnique,
                        ["partial"] = index.IsPartial
                    }).ToArray()
                };

                return new CanvasGraphNode(
                    Id: table.Name,
                    Label: table.Name,
                    Kind: table.Kind,
                    Group: table.Schema,
                    Fields: fields,
                    Details: details);
            })
            .ToArray();

        var edges = schema.Tables
            .SelectMany(table => table.ForeignKeys.Select(fk => new CanvasGraphEdge(
                Id: fk.Name,
                From: fk.Table,
                To: fk.PrincipalTable,
                Label: LabelFor(fk),
                Kind: "foreign-key",
                Details: new Dictionary<string, object?>
                {
                    ["columns"] = fk.Columns,
                    ["principalColumns"] = fk.PrincipalColumns,
                    ["onDelete"] = fk.OnDelete,
                    ["onUpdate"] = fk.OnUpdate
                })))
            .ToArray();

        var title = string.IsNullOrWhiteSpace(schema.SourcePath)
            ? $"{schema.ConnectionName} schema"
            : $"{schema.SourcePath} schema";

        return new CanvasGraphSpec(
            Title: title,
            Nodes: nodes,
            Edges: edges,
            Layout: "schema",
            Summary: $"{schema.Tables.Count} objects, {edges.Length} relationships");
    }

    public static UiWidgetTree ToGraphCanvasTree(DbSchemaModel schema)
    {
        var spec = ToGraphCanvasSpec(schema);
        return new UiWidgetTree(UiKitVocabulary.GraphCanvas, spec.ToProps());
    }

    public static CanvasGraphSpec RelationOfTwoObjects(
        string leftLabel = "Object 1",
        string rightLabel = "Object 2",
        string relationLabel = "relates to")
    {
        var leftId = StableNodeId(leftLabel, "object-1");
        var rightId = StableNodeId(rightLabel, "object-2");
        return new CanvasGraphSpec(
            Title: "Object relation",
            Nodes:
            [
                new CanvasGraphNode(leftId, leftLabel, Kind: "object"),
                new CanvasGraphNode(rightId, rightLabel, Kind: "object")
            ],
            Edges:
            [
                new CanvasGraphEdge("edge-1", leftId, rightId, relationLabel, "relation")
            ],
            Layout: "force",
            Summary: $"{leftLabel} {relationLabel} {rightLabel}");
    }

    public static UiWidgetTree RelationOfTwoObjectsTree(
        string leftLabel = "Object 1",
        string rightLabel = "Object 2",
        string relationLabel = "relates to")
    {
        var spec = RelationOfTwoObjects(leftLabel, rightLabel, relationLabel);
        return new UiWidgetTree(UiKitVocabulary.GraphCanvas, spec.ToProps());
    }

    private static string? BadgeFor(DbColumn column, bool isForeignKey)
    {
        var badges = new List<string>();
        if (column.PrimaryKeyOrdinal > 0)
            badges.Add("PK");
        if (isForeignKey)
            badges.Add("FK");
        if (!column.IsNullable && column.PrimaryKeyOrdinal == 0)
            badges.Add("NOT NULL");

        return badges.Count == 0 ? null : string.Join(", ", badges);
    }

    private static string LabelFor(DbForeignKey fk)
    {
        var left = fk.Columns.Count == 0 ? fk.Table : string.Join(", ", fk.Columns);
        var right = fk.PrincipalColumns.Count == 0 ? fk.PrincipalTable : string.Join(", ", fk.PrincipalColumns);
        return $"{left} -> {right}";
    }

    private static string StableNodeId(string label, string fallback)
    {
        var chars = label
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var id = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(id) ? fallback : id;
    }
}
