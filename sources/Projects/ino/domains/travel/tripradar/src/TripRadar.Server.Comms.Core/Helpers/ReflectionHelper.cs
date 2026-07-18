using System.Collections;
using System.Globalization;

namespace TripRadar.Server.Comms.Core.Helpers;

public static class ReflectionHelper
{
    public static bool IsComplexType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return !underlyingType.IsPrimitive &&
               underlyingType != typeof(string) &&
               underlyingType != typeof(DateTime) &&
               underlyingType != typeof(decimal) &&
               underlyingType is { IsEnum: false, IsClass: true };
    }

    public static bool HasValue(object? value, Type propertyType)
    {
        if (value == null) return false;

        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (underlyingType.IsValueType)
        {
            var defaultValue = Activator.CreateInstance(underlyingType);
            return !value.Equals(defaultValue);
        }

        return propertyType != typeof(string) || !string.IsNullOrWhiteSpace(value.ToString());
    }

    public static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null) return null;

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType.IsInstanceOfType(value)) return value;

        if (underlyingType == typeof(string) && TryConvertEnumerableToCsv(value, out var csvValue))
        {
            return csvValue;
        }

        if (underlyingType.IsEnum && value is string stringValue)
        {
            return Enum.TryParse(underlyingType, stringValue, true, out var enumValue) ? enumValue : null;
        }

        try
        {
            return Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryConvertEnumerableToCsv(object value, out string? csvValue)
    {
        if (value is string)
        {
            csvValue = null;
            return false;
        }

        if (value is not IEnumerable enumerable)
        {
            csvValue = null;
            return false;
        }

        var parts = (from object? item in enumerable select item?.ToString()?.Trim() into normalizedItem where !string.IsNullOrWhiteSpace(normalizedItem) select normalizedItem).ToList();

        csvValue = string.Join(",", parts);
        return true;
    }
}
