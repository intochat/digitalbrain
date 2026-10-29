using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace DigitalBrain;

public sealed partial class AppDefinition
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Constructs trusted app code using its widest public constructor, resolving services before optional defaults.</summary>
    public App Create(Func<Type, object?> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var constructors = AppType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).ToArray();
        if (constructors.Length == 0) { throw new InvalidOperationException($"{AppType.FullName} has no public constructor."); }
        var constructor = constructors[0];
        if (constructors.Length > 1 && constructors[1].GetParameters().Length == constructor.GetParameters().Length)
        {
            throw new InvalidOperationException($"{AppType.FullName} has ambiguous widest public constructors.");
        }
        var arguments = constructor.GetParameters().Select(parameter => services(parameter.ParameterType)
            ?? (parameter.HasDefaultValue ? parameter.DefaultValue
                : throw new InvalidOperationException($"App '{Manifest.Id}' needs a {parameter.ParameterType.Name} that the services do not provide."))).ToArray();
        try { return (App)constructor.Invoke(arguments); }
        catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Invokes and awaits one operation. The caller owns turn serialization, receipts and instance lifetime.</summary>
    public async Task Invoke(App instance, string operation, object? argument, JsonSerializerOptions? json = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!AppType.IsInstanceOfType(instance)) { throw new ArgumentException($"Instance must be a {AppType.FullName}.", nameof(instance)); }
        var method = FindOperation(operation) ?? throw new ArgumentException(
            $"App '{Manifest.Id}' has no operation '{operation}'. Operations: {string.Join(", ", Operations.Values.Select(m => m.Name).Order(StringComparer.Ordinal))}.", nameof(operation));
        var arguments = method.GetParameters().Select(parameter => parameter.ParameterType == typeof(CancellationToken)
            ? (object)ct : ConvertInput(argument, parameter.ParameterType, json ?? _json)).ToArray();
        object? result;
        try { result = method.Invoke(instance, arguments); }
        catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        switch (result)
        {
            case Task task: await task.ConfigureAwait(false); break;
            case ValueTask valueTask: await valueTask.ConfigureAwait(false); break;
        }
    }

    private static object? ConvertInput(object? argument, Type type, JsonSerializerOptions json)
    {
        if (argument is null)
        {
            if (type == typeof(string)) { return string.Empty; }
            if (type.IsValueType) { return Activator.CreateInstance(type); }
            try { return JsonSerializer.Deserialize("{}", type, json); }
            catch (Exception error) when (error is JsonException or NotSupportedException or InvalidOperationException) { return null; }
        }
        return type.IsInstanceOfType(argument) ? argument
            : JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(argument, argument.GetType(), json), type, json);
    }
}
