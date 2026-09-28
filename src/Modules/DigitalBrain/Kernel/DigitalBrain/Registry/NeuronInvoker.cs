using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.Core.Registry;

// Error is null when the call succeeded; otherwise it names a contract failure the caller can repair.
public sealed record NeuronCallResult(string NeuronId, JsonElement? Value, string? Error = null, string? Message = null);

// Calls any registered neuron method with JSON arguments, the way a model or a script names it.
public sealed class NeuronInvoker(IGrainFactory grains, NeuronRegistry registry)
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly NullabilityInfoContext Nullability = new();

    public async Task<NeuronCallResult> Invoke(string methodId, string neuronId, JsonElement arguments, CancellationToken cancellationToken)
    {
        try
        {
            var method = registry.FindMethod(methodId) ?? throw new KeyNotFoundException($"No neuron method '{methodId}'.");
            ArgumentException.ThrowIfNullOrWhiteSpace(neuronId);
            var values = Bind(method.Method, arguments, cancellationToken);
            var grain = grains.GetGrain(method.Contract.Interface, neuronId);
            object? returned;
            try { returned = method.Method.Invoke(grain, values); }
            catch (TargetInvocationException invocation) when (invocation.InnerException is not null) { throw invocation.InnerException; }
            var value = await Await(returned, method.Method.ReturnType).WaitAsync(cancellationToken).ConfigureAwait(false);
            return new(neuronId, value.Type is null ? null : JsonSerializer.SerializeToElement(value.Result, value.Type, Json));
        }
        catch (Exception error) when (Repairable(error))
        {
            return new(neuronId, null, error.GetType().Name, error.Message);
        }
    }

    private static object?[] Bind(MethodInfo method, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined or JsonValueKind.Null))
        { throw new ArgumentException("Arguments must be a JSON object."); }
        return [.. method.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == typeof(CancellationToken)) { return cancellationToken; }
            if (arguments.ValueKind == JsonValueKind.Object && Find(arguments, parameter.Name!) is { } supplied
                && supplied.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            { return supplied.Deserialize(parameter.ParameterType, Json); }
            if (parameter.HasDefaultValue) { return parameter.DefaultValue; }
            if (Nullability.Create(parameter).WriteState == NullabilityState.Nullable) { return null; }
            throw new ArgumentException($"The argument '{parameter.Name}' is required.");
        })];
    }

    private static JsonElement? Find(JsonElement arguments, string name)
    {
        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { return property.Value; }
        }
        return null;
    }

    private static async Task<(object? Result, Type? Type)> Await(object? returned, Type declared)
    {
        var task = returned switch
        {
            Task plain => plain,
            ValueTask plain => plain.AsTask(),
            _ => (Task)returned!.GetType().GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(returned, null)!,
        };
        await task.ConfigureAwait(false);
        if (!declared.IsGenericType) { return (null, null); }
        var type = declared.GetGenericArguments()[0];
        return (task.GetType().GetProperty(nameof(Task<int>.Result))!.GetValue(task), type);
    }

    private static bool Repairable(Exception error) => error is ArgumentException or KeyNotFoundException or JsonException or FormatException
        || error.GetType().Name.EndsWith("ValidationException", StringComparison.Ordinal)
        || error.GetType().Name.EndsWith("ConflictException", StringComparison.Ordinal);
}
