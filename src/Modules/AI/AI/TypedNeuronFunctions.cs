using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// One AIFunction per callable method of a neuron's grain type, generated from the kernel's
// descriptors so that what the model is shown is exactly what call accepts.
internal static class TypedNeuronFunctions
{
    internal static IEnumerable<AIFunction> For(INeuronInvoker invoker, NeuronId neuron, ISet<string> takenNames)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(takenNames);

        foreach (var descriptor in invoker.Describe(neuron))
        {
            var contract = invoker.ArgumentContractOf(descriptor.InterfaceAlias, descriptor.MethodAlias);
            var function = AIFunctionFactory.Create(
                async (JsonElement arguments, CancellationToken cancellationToken) =>
                {
                    var result = await invoker.InvokeAsync(neuron, descriptor.InterfaceAlias,
                        descriptor.MethodAlias, arguments, cancellationToken).ConfigureAwait(true);
                    return result?.GetRawText() ?? "null";
                },
                new AIFunctionFactoryOptions
                {
                    Name = UniqueName(neuron, descriptor.MethodAlias, takenNames),
                    Description = descriptor.Summary ?? $"Invoke {descriptor.InterfaceAlias}.{descriptor.MethodAlias} on neuron {neuron}.",
                    ConfigureParameterBinding = parameter => parameter.ParameterType == typeof(JsonElement)
                        ? new AIFunctionFactoryOptions.ParameterBindingOptions
                        {
                            BindParameter = (_, arguments) => BindArguments(arguments, contract),
                            ExcludeFromSchema = true,
                        }
                        : default,
                    ExcludeResultSchema = true,
                });
            yield return descriptor.ArgsSchema is { } schema
                ? new DescriptorFunction(function, ToolParameters(schema, contract?.CommandIdPropertyName)) : function;
        }
    }

    private static JsonElement ToolParameters(JsonElement schema, string? commandId)
    {
        var parameters = JsonNode.Parse(schema.GetRawText())!.AsObject();
        // A function receives an argument object even when the DTO's standalone schema allows null.
        // OpenAI's adapter binds this root type as a string; nested nullable schemas remain valid.
        parameters["type"] = "object";
        if (commandId is not null && parameters["required"] is JsonArray required)
        {
            for (var index = required.Count - 1; index >= 0; index--)
            {
                if (required[index]?.GetValue<string>() == commandId) { required.RemoveAt(index); }
            }
        }
        return JsonSerializer.SerializeToElement(parameters);
    }

    private static string UniqueName(NeuronId neuron, string methodAlias, ISet<string> takenNames)
    {
        var sanitised = new string($"{neuron}_{methodAlias}"
            .Select(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray());

        if (sanitised.Length > 60)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{neuron}/{methodAlias}")))[..16];
            sanitised = sanitised[..43] + "_" + hash;
        }

        // No separator can settle this on its own: both '_' and '-' are legal inside a neuron
        // name, so two different neurons can sanitise to the same prefix.
        var unique = sanitised;
        for (var suffix = 1; !takenNames.Add(unique); suffix++)
        {
            var ending = "_" + suffix.ToString(CultureInfo.InvariantCulture);
            unique = sanitised[..Math.Min(sanitised.Length, 64 - ending.Length)] + ending;
        }

        return unique;
    }

    // A model's arguments are already JSON. Only the command id the kernel needs goes through
    // the method's own contract, whose naming policy decides both its property name and shape.
    private static JsonElement BindArguments(AIFunctionArguments arguments, ArgumentContract? contract)
    {
        var supplied = new JsonObject();
        foreach (var (name, value) in arguments)
        {
            supplied[name] = value switch
            {
                null => null,
                JsonNode node => node.DeepClone(),
                JsonElement element => JsonNode.Parse(element.GetRawText()),
                _ => JsonSerializer.SerializeToNode(value, value.GetType(), contract?.Options),
            };
        }

        if (contract?.CommandIdPropertyName is { } commandId && !supplied.ContainsKey(commandId))
        {
            // A model must not be asked to invent a GUID, and a retry with the same id is the
            // kernel's own deduplication, so a call the model did not identify gets a fresh one.
            supplied[commandId] = JsonSerializer.SerializeToNode(CommandId.New(), contract.Options);
        }

        using var document = JsonDocument.Parse(supplied.ToJsonString());
        return document.RootElement.Clone();
    }

    // The factory binds the whole argument object; its inferred parameter schema is not the DTO schema.
    private sealed class DescriptorFunction(AIFunction function, JsonElement schema) : DelegatingAIFunction(function)
    {
        public override JsonElement JsonSchema => schema;
    }
}
