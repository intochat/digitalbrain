using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Orleans.Configuration;
using Orleans.Metadata;
using Orleans.Runtime;

namespace DigitalBrain.Core;

public sealed class DescriptorTable
{
    private readonly Dictionary<GrainType, MethodDescriptor[]> _descriptors = [];
    private readonly Dictionary<GrainType, string[]> _aliases = [];
    private readonly Dictionary<(string Interface, string Method), InvocationMetadata> _methods = [];

    public DescriptorTable(IOptions<GrainTypeOptions> options, GrainTypeResolver grainTypes, GrainInterfaceTypeResolver interfaceTypes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(grainTypes);
        ArgumentNullException.ThrowIfNull(interfaceTypes);
        Dictionary<string, Type> interfaces = new(StringComparer.Ordinal);
        Dictionary<Type, JsonSerializerOptions> contexts = [];
        var summaries = new XmlDocSummaries();
        foreach (var grainClass in options.Value.Classes.Where(type => type is { IsClass: true, IsAbstract: false } && typeof(INeuron).IsAssignableFrom(type)))
        {
            DescriptorRules.Validate(grainClass);
            var grainType = grainTypes.GetGrainType(grainClass);
            List<string> aliases = [];
            List<MethodDescriptor> descriptors = [];
            foreach (var contract in grainClass.GetInterfaces().Where(type => typeof(INeuron).IsAssignableFrom(type)))
            {
                var alias = contract.GetCustomAttribute<AliasAttribute>()!.Alias;
                aliases.Add(alias);
                if (interfaces.TryGetValue(alias, out var existing) && existing != contract)
                {
                    throw new InvalidOperationException($"Interface alias '{alias}' is shared by '{existing.FullName}' and '{contract.FullName}'. Choose unique interface aliases.");
                }

                interfaces[alias] = contract;
                if (contract == typeof(INeuron))
                {
                    continue;
                }

                var json = JsonOptions(contract, contexts);
                foreach (var method in contract.GetMethods())
                {
                    var methodAlias = method.GetCustomAttribute<AliasAttribute>()!.Alias;
                    var key = (alias, methodAlias);
                    if (!_methods.TryGetValue(key, out var metadata))
                    {
                        var parameters = method.GetParameters();
                        var argumentType = parameters.FirstOrDefault(parameter => parameter.ParameterType != typeof(CancellationToken))?.ParameterType;
                        var resultType = method.ReturnType == typeof(Task) ? null : method.ReturnType.GenericTypeArguments[0];
                        var argumentsJson = TypeInfo(argumentType, json, method);
                        var resultJson = TypeInfo(resultType, json, method);
                        var descriptor = new MethodDescriptor(alias, methodAlias, method.IsDefined(typeof(ReadOnlyAttribute)),
                            Schema(argumentsJson), Schema(resultJson), summaries.For(method));
                        metadata = new(contract, interfaceTypes.GetGrainInterfaceType(contract), argumentsJson, resultJson,
                            CompileCall(method), CompileResult(method.ReturnType), descriptor);
                        _methods.Add(key, metadata);
                    }

                    descriptors.Add(metadata.Descriptor);
                }
            }

            _aliases.Add(grainType, [.. aliases]);
            _descriptors.Add(grainType, [.. descriptors]);
        }
    }

    public IReadOnlyList<MethodDescriptor> For(GrainType grainType) => _descriptors.GetValueOrDefault(grainType) ?? [];

    public MethodDescriptor Get(string interfaceAlias, string methodAlias) => Method(interfaceAlias, methodAlias).Descriptor;

    public IReadOnlyList<string> InterfaceAliasesOf(GrainType grainType) => _aliases.GetValueOrDefault(grainType) ?? [];

    public CommandDescriptor DescriptorFor(GrainType grainType, string methodAlias)
    {
        var matches = For(grainType).Where(method => method.MethodAlias == methodAlias).ToArray();
        return matches.Length switch
        {
            1 => new(matches[0].InterfaceAlias, methodAlias),
            0 => throw new InvalidOperationException($"Grain type '{grainType}' has no method alias '{methodAlias}'. Use a declared module method alias."),
            _ => throw new InvalidOperationException($"Grain type '{grainType}' has ambiguous method alias '{methodAlias}' on interfaces {string.Join(", ", matches.Select(method => method.InterfaceAlias))}. Choose a unique method alias."),
        };
    }

    internal InvocationMetadata Method(string interfaceAlias, string methodAlias)
        => _methods.TryGetValue((interfaceAlias, methodAlias), out var method)
            ? method
            : throw new ArgumentException($"Unknown interface and method '{interfaceAlias}/{methodAlias}'. Use describe to find a declared method.");

    private static JsonSerializerOptions JsonOptions(Type contract, Dictionary<Type, JsonSerializerOptions> contexts)
    {
        var contextType = contract.Assembly.GetCustomAttribute<NeuronJsonContextAttribute>()?.ContextType
            ?? throw new InvalidOperationException($"Interface '{contract.FullName}' in assembly '{contract.Assembly.GetName().Name}' needs an assembly NeuronJsonContext attribute naming its JSON context.");
        if (!contexts.TryGetValue(contextType, out var options))
        {
            var context = contextType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as JsonSerializerContext
                ?? throw new InvalidOperationException($"Interface '{contract.FullName}' needs JSON context '{contextType.FullName}' to expose public static Default.");
            options = new(context.Options)
            {
                TypeInfoResolver = context,
                RespectRequiredConstructorParameters = true,
                RespectNullableAnnotations = true,
            };
            contexts.Add(contextType, options);
        }

        return options;
    }

    private static JsonTypeInfo? TypeInfo(Type? type, JsonSerializerOptions options, MethodInfo method)
    {
        if (type is null)
        {
            return null;
        }

        try
        {
            return options.GetTypeInfo(type);
        }
        catch (Exception error) when (error is NotSupportedException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Interface '{method.DeclaringType!.FullName}', method '{method.Name}' needs JSON type '{type.FullName}'. Add it to the assembly's source-generated JSON context.", error);
        }
    }

    private static JsonElement? Schema(JsonTypeInfo? typeInfo)
    {
        if (typeInfo is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(typeInfo.GetJsonSchemaAsNode().ToJsonString());
        return document.RootElement.Clone();
    }

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
    Type Interface,
    GrainInterfaceType InterfaceType,
    JsonTypeInfo? ArgumentsJson,
    JsonTypeInfo? ResultJson,
    Func<object, object?, CancellationToken, Task> Invoke,
    Func<Task, object?>? Result,
    MethodDescriptor Descriptor);
