using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal static class TypedNeuronFunctions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static IEnumerable<AIFunction> For(INeuronInvoker invoker, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(names);
        foreach (var name in names)
        {
            if (!NeuronId.TryParse(name, out var neuron))
            {
                continue;
            }

            foreach (var descriptor in invoker.Describe(neuron))
            {
                var function = AIFunctionFactory.Create(
                    async (JsonElement arguments, CancellationToken cancellationToken) =>
                    {
                        if (!descriptor.IsReadOnly && !arguments.TryGetProperty("id", out _))
                        {
                            // A model must not invent a GUID; retrying with the same id is the kernel's deduplication.
                            var command = JsonNode.Parse(arguments.GetRawText())!.AsObject();
                            command["id"] = JsonSerializer.SerializeToNode(CommandId.New(), Json);
                            arguments = JsonSerializer.SerializeToElement(command, Json);
                        }

                        return JsonSerializer.Serialize(await invoker.InvokeAsync(neuron, descriptor.InterfaceAlias,
                            descriptor.MethodAlias, arguments, cancellationToken).ConfigureAwait(true), Json);
                    },
                    new AIFunctionFactoryOptions
                    {
                        Name = new string($"{neuron}_{descriptor.MethodAlias}"
                            .Select(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray()),
                        Description = descriptor.Summary ?? $"Invoke {descriptor.InterfaceAlias}.{descriptor.MethodAlias} on neuron {neuron}.",
                        ConfigureParameterBinding = static parameter => parameter.ParameterType == typeof(JsonElement)
                            ? new AIFunctionFactoryOptions.ParameterBindingOptions
                            {
                                BindParameter = static (_, arguments) => JsonSerializer.SerializeToElement(arguments, Json),
                                ExcludeFromSchema = true,
                            }
                            : default,
                        ExcludeResultSchema = true,
                    });
                yield return descriptor.ArgsSchema is { } schema ? new DescriptorFunction(function, schema) : function;
            }
        }
    }

    // The factory binds the whole argument object; its inferred parameter schema is not the DTO schema.
    private sealed class DescriptorFunction(AIFunction function, JsonElement schema) : DelegatingAIFunction(function)
    {
        public override JsonElement JsonSchema => schema;
    }
}
