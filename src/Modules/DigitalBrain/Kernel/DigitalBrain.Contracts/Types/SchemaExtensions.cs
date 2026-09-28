using System.Text.Json.Nodes;

namespace DigitalBrain.Contracts.Types;

public enum McpFormPrimitive
{
    String,
    Number,
    Boolean,
    Enum,
    MultiChoiceEnum,
}

public static class SchemaExtensions
{
    public static McpFormPrimitive? ToMcpFormPrimitive(this SemanticType type) => type.Kind switch
    {
        FieldKind.PlainText => McpFormPrimitive.String,
        FieldKind.Number => McpFormPrimitive.Number,
        FieldKind.Boolean => McpFormPrimitive.Boolean,
        FieldKind.Date => McpFormPrimitive.String,
        FieldKind.DateTime => McpFormPrimitive.String,
        FieldKind.Email => McpFormPrimitive.String,
        FieldKind.Url => McpFormPrimitive.String,
        FieldKind.Choice => McpFormPrimitive.Enum,
        FieldKind.MultiChoice => McpFormPrimitive.MultiChoiceEnum,
        _ => null,
    };

    public static JsonObject ToJsonSchema(this SemanticType type)
    {
        var schema = type.Kind switch
        {
            FieldKind.MultiChoice => new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
            },
            FieldKind.Number => new JsonObject { ["type"] = "number" },
            FieldKind.Boolean => new JsonObject { ["type"] = "boolean" },
            _ => new JsonObject { ["type"] = "string" },
        };

        if (JsonFormat(type.Kind) is { } format)
        {
            schema["format"] = format;
        }

        schema["x-intochat-id"] = type.Id;
        schema["x-intochat-kind"] = type.Kind.ToString();
        schema["x-intochat-primitive"] = type.Primitive.ToString();
        schema["x-intochat-sensitivity"] = type.Sensitivity.ToString();
        schema["x-intochat-llm-exposure"] = type.LlmExposure.ToString();
        schema["x-intochat-input-widget"] = type.InputWidget.ToString();
        schema["x-intochat-display-widget"] = type.DisplayWidget.ToString();
        schema["x-intochat-redactor"] = type.Redactor.ToString();
        schema["x-intochat-indexable"] = type.Indexable;
        schema["x-intochat-mcp-form"] = type.ToMcpFormPrimitive() is not null;
        return schema;
    }

    private static string? JsonFormat(FieldKind kind) => kind switch
    {
        FieldKind.Date => "date",
        FieldKind.DateTime => "date-time",
        FieldKind.Email => "email",
        FieldKind.Url => "uri",
        _ => null,
    };
}