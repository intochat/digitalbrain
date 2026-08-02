using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed record BoundProbeResponse(string Note) : Synapse;

public sealed record BoundProbeRequest : RequestSynapse<BoundProbeResponse>
{
    public BoundProbeRequest(string subject)
        : this(subject, CommandId.New())
    {
    }

    [JsonConstructor]
    public BoundProbeRequest(string subject, CommandId commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        Subject = subject.Trim();
        CommandId = commandId;
    }

    public string Subject { get; init; }

    public CommandId CommandId { get; init; }
}

public sealed record PlainProbeRequest(string Note) : RequestSynapse<BoundProbeResponse>;

public sealed class CapabilityArgumentBinding
{
    [Fact(DisplayName =
        "model arguments bind by camelCase name and the binder mints the command id the model never saw")]
    public void BinderMintsCommandIdWhenModelOmitsIt()
    {
        var bound = Assert.IsType<BoundProbeRequest>(Bind(
            typeof(BoundProbeRequest),
            """{"subject":"  hello  "}"""));

        Assert.Equal("hello", bound.Subject);
        Assert.NotEqual(Guid.Empty, bound.CommandId.Value);
    }

    [Fact(DisplayName =
        "a model-supplied commandId is discarded; the binder owns command identity")]
    public void BinderDiscardsModelSuppliedCommandId()
    {
        var supplied = Guid.NewGuid();
        var json = "{\"subject\":\"hello\",\"commandId\":{\"value\":\"" + supplied + "\"}}";
        var bound = Assert.IsType<BoundProbeRequest>(Bind(typeof(BoundProbeRequest), json));

        Assert.NotEqual(Guid.Empty, bound.CommandId.Value);
        Assert.NotEqual(supplied, bound.CommandId.Value);
    }

    [Fact(DisplayName =
        "a request synapse without command identity binds untouched")]
    public void BinderLeavesCommandlessSynapsesAlone()
    {
        var bound = Assert.IsType<PlainProbeRequest>(Bind(
            typeof(PlainProbeRequest),
            """{"note":"plain"}"""));

        Assert.Equal("plain", bound.Note);
    }

    [Fact(DisplayName =
        "the model-facing schema hides commandId while the catalog schema keeps it")]
    public void ModelSchemaHidesCommandId()
    {
        var catalogSchema = CapabilitySchema.For(typeof(BoundProbeRequest));
        Assert.Contains("commandId", catalogSchema, StringComparison.Ordinal);

        var modelSchema = Assert.IsType<JsonObject>(
            JsonNode.Parse(SynapseCapabilityTool.ModelSchemaFor(catalogSchema)));
        var properties = Assert.IsType<JsonObject>(modelSchema["properties"]);
        Assert.True(properties.ContainsKey("subject"));
        Assert.False(properties.ContainsKey("commandId"), "The model-facing schema exposes commandId.");

        if (modelSchema["required"] is JsonArray required)
        {
            Assert.DoesNotContain(
                required,
                entry => entry is JsonValue value
                    && value.TryGetValue<string>(out var name)
                    && string.Equals(name, "commandId", StringComparison.Ordinal));
        }
    }

    private static Synapse Bind(Type requestType, string modelArgumentsJson)
    {
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modelArgumentsJson)
            ?? throw new InvalidOperationException("Model arguments parsed to null.");
        return SynapseCapabilityTool.BindModelArguments(
            requestType,
            "moduletests.bound-probe-request",
            arguments.Select(pair => new KeyValuePair<string, object?>(pair.Key, (object?)pair.Value)));
    }
}
