using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

[GrainType("recipe")]
public sealed class RecipeNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    private const string Instruct = "Instruct";

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.Signal.Type == Instruct)
        {
            return;
        }

        var body = await LatestAsync(Instruct).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(body) || JsonNode.Parse(body) is not JsonObject recipe)
        {
            return;
        }

        var on = Text(recipe, "on");
        if (on.Length > 0 && !string.Equals(on, delivery.Signal.Type, StringComparison.Ordinal))
        {
            return;
        }

        if (Text(recipe, "ask") is { Length: > 0 } ask)
        {
            var text = Field(delivery.Signal.Body, Text(recipe, "textField") is { Length: > 0 } field ? field : "text");
            if (!NeuronId.TryParse(ask, out var agent))
            {
                throw new ArgumentException($"'{ask}' is not a neuron name.");
            }

            await FireAsync(Signal.Create("Ask", JsonSerializer.Serialize(new { text })), agent, delivery.CorrelationId, cancellationToken)
                .ConfigureAwait(true);
            return;
        }

        if (recipe["call"] is not JsonObject call)
        {
            return;
        }

        double? numeric = null;
        if (Text(recipe, "length") is { Length: > 0 } lengthField)
        {
            numeric = Field(delivery.Signal.Body, lengthField).Length;
        }
        else if (Text(recipe, "read") is { Length: > 0 } readName)
        {
            numeric = await ReadNumericAsync(readName, Text(recipe, "state"), Text(recipe, "field"), cancellationToken)
                .ConfigureAwait(true);
        }

        var argsNode = call["args"]?.DeepClone() ?? new JsonObject();
        Substitute(argsNode, numeric, delivery);
        if (!NeuronId.TryParse(Text(call, "neuron"), out var target))
        {
            throw new ArgumentException("A recipe call needs a neuron name.");
        }

        var invoker = ServiceProvider.GetRequiredService<INeuronInvoker>();
        using var document = JsonDocument.Parse(argsNode.ToJsonString());
        await invoker.InvokeAsync(target, Text(call, "interface"), Text(call, "method"), document.RootElement.Clone(), cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task<string> LatestAsync(string type)
        => (await ReadState().ConfigureAwait(true)).FirstOrDefault(d => d.Signal.Type == type)?.Signal.Body ?? "";

    private async Task<double> ReadNumericAsync(string neuron, string stateType, string field, CancellationToken cancellationToken)
    {
        if (!NeuronId.TryParse(neuron, out var id))
        {
            throw new ArgumentException($"'{neuron}' is not a neuron name.");
        }

        var state = await GrainFactory.GetGrain<INeuron>(id.ToGrainId()).ReadState().WaitAsync(cancellationToken)
            .ConfigureAwait(true);
        var match = state.FirstOrDefault(d => d.Signal.Type == stateType)
            ?? throw new InvalidOperationException($"Neuron '{id}' has no latest '{stateType}'.");
        using var document = JsonDocument.Parse(match.Signal.Body);
        if (!document.RootElement.TryGetProperty(field, out var value) || !value.TryGetDouble(out var number))
        {
            throw new InvalidOperationException($"Latest '{stateType}' on '{id}' has no numeric '{field}'.");
        }

        return number;
    }

    private static string Field(string body, string name)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        return document.RootElement.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
    }

    private static string Text(JsonObject json, string name)
        => json[name] is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : "";

    private static void Substitute(JsonNode node, double? numeric, SignalDelivery delivery)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(property => property.Key).ToArray())
                {
                    if (obj[key] is JsonValue value && value.TryGetValue<string>(out var token) && Replace(token, numeric, delivery) is { } replacement)
                    {
                        obj[key] = replacement;
                    }
                    else if (obj[key] is { } child)
                    {
                        Substitute(child, numeric, delivery);
                    }
                }

                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    if (array[index] is JsonValue value && value.TryGetValue<string>(out var token) && Replace(token, numeric, delivery) is { } replacement)
                    {
                        array[index] = replacement;
                    }
                    else if (array[index] is { } child)
                    {
                        Substitute(child, numeric, delivery);
                    }
                }

                break;
        }
    }

    private static JsonNode? Replace(string token, double? numeric, SignalDelivery delivery)
        => token switch
        {
            "$read" or "$length" => JsonValue.Create(numeric
                ?? throw new InvalidOperationException($"Recipe token '{token}' needs a numeric value from read or length.")),
            "$signalId" => JsonValue.Create(delivery.SignalId.ToString()),
            "$commandId" => JsonValue.Create(Guid.NewGuid().ToString("D")),
            _ => null,
        };
}
