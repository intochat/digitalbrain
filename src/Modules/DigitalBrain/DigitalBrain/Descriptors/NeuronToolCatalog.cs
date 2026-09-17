using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Metadata;
using Orleans.Runtime;

namespace DigitalBrain.Core;

// JSON metadata belongs to explicitly exported tools, never to grain activation or command execution.
internal sealed class NeuronToolCatalog
{
    private readonly Dictionary<GrainType, MethodDescriptor[]> _descriptors = [];
    private readonly Dictionary<(string Interface, string Method), InvocationMetadata> _methods = [];

    public NeuronToolCatalog(IOptions<GrainTypeOptions> options, GrainTypeResolver grainTypes, GrainInterfaceTypeResolver interfaceTypes)
    {
        Dictionary<Assembly, JsonSerializerOptions> contexts = [];
        var summaries = new XmlDocSummaries();
        foreach (var grainClass in options.Value.Classes.Where(type => type is { IsClass: true, IsAbstract: false }))
        {
            List<MethodDescriptor> descriptors = [];
            foreach (var contract in grainClass.GetInterfaces().Where(type => typeof(IGrain).IsAssignableFrom(type)))
            {
                foreach (var method in contract.GetMethods())
                {
                    if (method.GetCustomAttribute<NeuronToolAttribute>() is not { } tool)
                    {
                        continue;
                    }

                    var interfaceAlias = contract.GetCustomAttribute<AliasAttribute>()?.Alias ?? contract.FullName!;
                    var methodAlias = method.GetCustomAttribute<AliasAttribute>()?.Alias ?? method.Name;
                    var key = (interfaceAlias, methodAlias);
                    if (!_methods.TryGetValue(key, out var metadata))
                    {
                        metadata = Build(method, tool, interfaceAlias, methodAlias, interfaceTypes.GetGrainInterfaceType(contract),
                            JsonOptions(contract.Assembly, contexts), summaries.For(method));
                        _methods.Add(key, metadata);
                    }
                    else if (metadata.Method != method)
                    {
                        throw new InvalidOperationException($"Tool '{interfaceAlias}/{methodAlias}' is declared by both '{metadata.Method}' and '{method}'. Use distinct aliases.");
                    }
                    descriptors.Add(metadata.Descriptor);
                }
            }
            if (descriptors.Count > 0)
            {
                _descriptors.Add(grainTypes.GetGrainType(grainClass), [.. descriptors]);
            }
        }
    }

    internal IReadOnlyList<MethodDescriptor> For(GrainType grainType) => _descriptors.GetValueOrDefault(grainType) ?? [];

    internal InvocationMetadata Method(string interfaceAlias, string methodAlias)
        => _methods.TryGetValue((interfaceAlias, methodAlias), out var method)
            ? method
            : throw new ArgumentException($"Unknown tool '{interfaceAlias}/{methodAlias}'. Only methods marked [NeuronTool] are callable.");

    internal ArgumentContract? ArgumentContractOf(string interfaceAlias, string methodAlias)
    {
        var method = Method(interfaceAlias, methodAlias);
        return method.ArgumentsJson is { } arguments ? new(arguments.Options, method.CommandIdPropertyName) : null;
    }

    private static InvocationMetadata Build(MethodInfo method, NeuronToolAttribute tool, string interfaceAlias,
        string methodAlias, GrainInterfaceType interfaceType, JsonSerializerOptions json, string? summary)
    {
        var parameters = method.GetParameters();
        var arguments = parameters.Where(parameter => parameter.ParameterType != typeof(CancellationToken)).ToArray();
        var returnType = method.ReturnType;
        // This is the tool adapter's wire shape, not a restriction on ordinary grain methods.
        if (method.ContainsGenericParameters || arguments.Length > 1 || parameters.Any(parameter => parameter.ParameterType.IsByRef)
            || (returnType != typeof(Task) && (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>))))
        {
            throw new InvalidOperationException($"Tool '{interfaceAlias}/{methodAlias}' must return Task or Task<T> and accept at most one JSON argument plus cancellation. Use an adapter method for other signatures.");
        }

        var argumentsJson = arguments.Length == 0 ? null : json.GetTypeInfo(arguments[0].ParameterType);
        if (argumentsJson is not null && argumentsJson.Kind != JsonTypeInfoKind.Object)
        {
            throw new InvalidOperationException($"Tool '{interfaceAlias}/{methodAlias}' requires a JSON object argument.");
        }
        var commandId = argumentsJson is not null && typeof(Command).IsAssignableFrom(argumentsJson.Type)
            ? argumentsJson.Properties.FirstOrDefault(property => property.PropertyType == typeof(CommandId)
                && property.AttributeProvider is MemberInfo { Name: nameof(Command.Id) } && property.Get is not null)?.Name
                ?? throw new InvalidOperationException($"Tool '{interfaceAlias}/{methodAlias}' must serialize its command Id.")
            : null;
        var resultType = returnType == typeof(Task) ? null : returnType.GenericTypeArguments[0];
        var returnsNeuron = resultType is not null && typeof(INeuron).IsAssignableFrom(resultType);
        var resultJson = returnsNeuron ? NeuronReferenceJson.Default.NeuronId : resultType is null ? null : json.GetTypeInfo(resultType);
        var descriptor = new MethodDescriptor(interfaceAlias, methodAlias, tool.IsReadOnly,
            Schema(argumentsJson), Schema(resultJson), summary);
        return new(method, interfaceType, argumentsJson, commandId, CompileCall(method), CompileResult(returnType), returnsNeuron, descriptor, resultJson);
    }

    private static JsonSerializerOptions JsonOptions(Assembly assembly, Dictionary<Assembly, JsonSerializerOptions> contexts)
    {
        if (!contexts.TryGetValue(assembly, out var options))
        {
            var contextType = assembly.GetCustomAttribute<NeuronJsonContextAttribute>()?.ContextType;
            options = contextType?.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is JsonSerializerContext context
                ? new(context.Options) { TypeInfoResolver = context }
                : new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
            options.RespectRequiredConstructorParameters = true;
            options.RespectNullableAnnotations = true;
            contexts.Add(assembly, options);
        }
        return options;
    }

    private static JsonElement? Schema(JsonTypeInfo? typeInfo)
        => typeInfo is null ? null : JsonSerializer.SerializeToElement(typeInfo.GetJsonSchemaAsNode());

    private static Func<object, object?, CancellationToken, Task> CompileCall(MethodInfo method)
    {
        var proxy = Expression.Parameter(typeof(object));
        var argument = Expression.Parameter(typeof(object));
        var cancellation = Expression.Parameter(typeof(CancellationToken));
        var parameters = method.GetParameters().Select(parameter => parameter.ParameterType == typeof(CancellationToken)
            ? (Expression)cancellation : Expression.Convert(argument, parameter.ParameterType));
        var call = Expression.Call(Expression.Convert(proxy, method.DeclaringType!), method, parameters);
        return Expression.Lambda<Func<object, object?, CancellationToken, Task>>(call, proxy, argument, cancellation).Compile();
    }

    private static Func<Task, object?>? CompileResult(Type returnType)
    {
        if (returnType == typeof(Task))
        {
            return null;
        }
        var task = Expression.Parameter(typeof(Task));
        var result = Expression.Property(Expression.Convert(task, returnType), "Result");
        return Expression.Lambda<Func<Task, object?>>(Expression.Convert(result, typeof(object)), task).Compile();
    }
}

internal sealed record InvocationMetadata(
    MethodInfo Method,
    GrainInterfaceType InterfaceType,
    JsonTypeInfo? ArgumentsJson,
    string? CommandIdPropertyName,
    Func<object, object?, CancellationToken, Task> Invoke,
    Func<Task, object?>? Result,
    bool ReturnsNeuron,
    MethodDescriptor Descriptor,
    JsonTypeInfo? ResultJson);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NeuronId))]
internal sealed partial class NeuronReferenceJson : JsonSerializerContext;
