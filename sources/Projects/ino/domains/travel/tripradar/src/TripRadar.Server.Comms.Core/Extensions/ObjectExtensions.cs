using System.Reflection;
using System.Text.Json;

namespace TripRadar.Server.Comms.Core.Extensions;

/// <summary>
/// Extension methods for object operations including cloning and value checking.
/// </summary>
public static class ObjectExtensions
{
    /// <summary>
    /// Creates a shallow clone of the specified object by copying all public readable/writable properties.
    /// This overload requires a parameterless constructor for better performance.
    /// </summary>
    /// <typeparam name="T">The type of object to clone.</typeparam>
    /// <param name="original">The original object to clone.</param>
    /// <returns>A new instance with copied property values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when original is null.</exception>
    public static T ShallowCloneNew<T>(this T original) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(original);

        var type = typeof(T);
        var clone = new T();

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true });

        foreach (var property in properties)
        {
            try
            {
                var value = property.GetValue(original);
                property.SetValue(clone, value);
            }
            catch (Exception)
            {
                // Skip properties that can't be copied (e.g., computed properties)
                continue;
            }
        }

        return clone;
    }

    /// <summary>
    /// Creates a shallow clone of the specified object by copying all public readable/writable properties.
    /// This overload works with types that don't have a parameterless constructor.
    /// </summary>
    /// <typeparam name="T">The type of object to clone.</typeparam>
    /// <param name="original">The original object to clone.</param>
    /// <returns>A new instance with copied property values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when original is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the type cannot be instantiated.</exception>
    public static T ShallowClone<T>(this T original) where T : class
    {
        ArgumentNullException.ThrowIfNull(original);

        var type = typeof(T);
        if (Activator.CreateInstance(type) is not T clone)
            throw new InvalidOperationException($"Cannot create instance of type {type.Name}");

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true });

        foreach (var property in properties)
        {
            try
            {
                var value = property.GetValue(original);
                property.SetValue(clone, value);
            }
            catch (Exception)
            {
                // Skip properties that can't be copied (e.g., computed properties)
                continue;
            }
        }

        return clone;
    }

    /// <summary>
    /// Determines if a value is null, empty, or represents a default value for its type.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value is null, empty, or default; otherwise false.</returns>
    public static bool IsNullOrDefault(this object? value)
    {
        if (value == null)
            return true;

        var type = value.GetType();

        return type.Name switch
        {
            nameof(String) => string.IsNullOrWhiteSpace((string)value),
            nameof(Int32) => (int)value == 0,
            nameof(Int64) => (long)value == 0,
            nameof(Double) => Math.Abs((double)value) < double.Epsilon,
            nameof(Decimal) => (decimal)value == 0m,
            nameof(Boolean) => false, // Boolean always has a value, never consider default
            nameof(DateTime) => (DateTime)value == default,
            nameof(Guid) => (Guid)value == Guid.Empty,
            _ => type.IsValueType && value.Equals(Activator.CreateInstance(type))
        };
    }

    /// <summary>
    /// Determines if a value is not null, not empty, and not a default value for its type.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value has a meaningful value; otherwise false.</returns>
    public static bool HasValue(this object? value)
    {
        return !value.IsNullOrDefault();
    }

    public static string? SerializeParameters(this object? parameters)
    {
        return parameters == null ? null : JsonSerializer.Serialize(parameters);
    }
}
