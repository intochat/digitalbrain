using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Flutter;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using DigitalBrain.Tasks;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.Tests.Contracts;

public sealed class CapabilityToolBinding
{
    [Fact(DisplayName =
        "every accepted capability request synapse is model-bindable: one public constructor or exactly one [JsonConstructor]")]
    public void AcceptedRequestSynapsesAreModelBindable()
    {
        var offenders = new List<string>();
        foreach (var (descriptor, type) in AcceptedRequestSynapses())
        {
            var constructors = type.GetConstructors();
            var elected = type
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Count(constructor => constructor.IsDefined(typeof(JsonConstructorAttribute), inherit: false));
            if (constructors.Length > 1 && elected != 1)
            {
                offenders.Add(
                    $"{type.FullName} ({descriptor.ContractId} v{descriptor.SchemaVersion}): "
                    + $"{constructors.Length} public constructors, {elected} marked [JsonConstructor]");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact(DisplayName =
        "provider tools bind from a model call that omits commandId; the binder mints command identity")]
    public void ProviderToolsBindWithoutModelSuppliedCommandId()
    {
        var search = Assert.IsType<GmailSearchRequest>(Bind(
            typeof(GmailSearchRequest),
            "db.google.gmail-search-request",
            """{"query":"label:inbox","maxResults":1}"""));
        Assert.Equal("label:inbox", search.Query);
        Assert.Equal(1, search.MaxResults);
        Assert.NotEqual(Guid.Empty, search.CommandId.Value);

        var defaulted = Assert.IsType<GmailSearchRequest>(Bind(
            typeof(GmailSearchRequest),
            "db.google.gmail-search-request",
            """{"query":"is:unread"}"""));
        Assert.Equal(GmailSearchRequest.DefaultMaxResults, defaulted.MaxResults);

        var get = Assert.IsType<GmailGetMessageRequest>(Bind(
            typeof(GmailGetMessageRequest),
            "db.google.gmail-get-message-request",
            """{"messageId":"msg-42"}"""));
        Assert.Equal("msg-42", get.MessageId);
        Assert.NotEqual(Guid.Empty, get.CommandId.Value);

        var intent = Assert.IsType<GmailRequest>(Bind(
            typeof(GmailRequest),
            "db.google.gmail-request",
            """{"intent":"read my last three emails"}"""));
        Assert.Equal("read my last three emails", intent.Intent);
        Assert.NotEqual(Guid.Empty, intent.CommandId.Value);

        var salesforce = Assert.IsType<SalesforceRequest>(Bind(
            typeof(SalesforceRequest),
            "db.salesforce.request",
            """{"intent":"describe account Acme"}"""));
        Assert.Equal("describe account Acme", salesforce.Intent);
        Assert.NotEqual(Guid.Empty, salesforce.CommandId.Value);
    }

    [Fact(DisplayName =
        "model-facing tool schemas hide commandId while catalog schemas keep it for programmatic callers")]
    public void ModelSchemasHideCommandId()
    {
        var catalogCarriesCommandId = false;
        foreach (var (descriptor, _) in AcceptedRequestSynapses())
        {
            if (descriptor.JsonSchema.Contains("commandId", StringComparison.Ordinal))
            {
                catalogCarriesCommandId = true;
            }

            var schema = Assert.IsType<JsonObject>(
                JsonNode.Parse(SynapseCapabilityTool.ModelSchemaFor(descriptor.JsonSchema)));
            if (schema["properties"] is JsonObject properties)
            {
                Assert.False(
                    properties.ContainsKey("commandId"),
                    $"{descriptor.ContractId} exposes commandId to the model.");
            }

            if (schema["required"] is JsonArray required)
            {
                Assert.DoesNotContain(
                    required,
                    entry => entry is JsonValue value
                        && value.TryGetValue<string>(out var name)
                        && string.Equals(name, "commandId", StringComparison.Ordinal));
            }
        }

        Assert.True(catalogCarriesCommandId, "No catalog schema carries commandId; the gate lost its subject.");
    }

    private static Synapse Bind(Type requestType, string contractId, string modelArgumentsJson)
    {
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modelArgumentsJson)
            ?? throw new InvalidOperationException("Model arguments parsed to null.");
        return SynapseCapabilityTool.BindModelArguments(
            requestType,
            contractId,
            arguments.Select(pair => new KeyValuePair<string, object?>(pair.Key, (object?)pair.Value)));
    }

    private static List<(SynapseCapabilityDescriptor Descriptor, Type Type)> AcceptedRequestSynapses()
    {
        ICompiledModule[] modules =
        [
            new AIModule(),
            new FlutterModule(),
            new GoogleModule(),
            new SalesforceModule(),
            new TasksModule(),
            new TimeModule(),
        ];
        var catalog = ActiveCapabilityCatalog.Create(modules);
        var map = ActiveModuleContractTypeMap.Create(modules, catalog);

        var accepted = new List<(SynapseCapabilityDescriptor, Type)>();
        foreach (var module in catalog.Modules)
        {
            foreach (var neuron in module.Neurons)
            {
                foreach (var synapse in neuron.Accepted)
                {
                    if (!map.TryGetSynapseType(synapse.ContractId, synapse.SchemaVersion, out var type)
                        || type is null
                        || !IsRequestSynapse(type))
                    {
                        continue;
                    }

                    accepted.Add((synapse, type));
                }
            }
        }

        Assert.Contains(accepted, entry => entry.Item2 == typeof(GmailSearchRequest));
        Assert.Contains(accepted, entry => entry.Item2 == typeof(GmailGetMessageRequest));
        Assert.Contains(accepted, entry => entry.Item2 == typeof(GmailRequest));
        Assert.Contains(accepted, entry => entry.Item2 == typeof(SalesforceRequest));
        return accepted;
    }

    private static bool IsRequestSynapse(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(RequestSynapse<>))
            {
                return true;
            }
        }

        return false;
    }
}
