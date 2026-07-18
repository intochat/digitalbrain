using System.Reflection;
using System.Collections.Concurrent;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Comms.Core.Helpers;
using TripRadar.Server.Infrastructure.Contracts;

namespace TripRadar.Server.Infrastructure.Services;

public class PreferenceMappingService : IPreferenceMappingService
{
    private static readonly ConcurrentDictionary<Type, PropertyMapping[]> _mappingCache = new();

    public void ApplyPreferences<TRequest>(TRequest request, Dictionary<string, object> preferences) where TRequest : class
    {
        var mappings = GetMappings(typeof(TRequest));
        var mappingsByPreferenceName = mappings
            .GroupBy(mapping => mapping.NormalizedPreferenceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var (preferenceName, preferenceValue) in preferences)
        {
            if (mappingsByPreferenceName.TryGetValue(NameNormalizer.Normalize(preferenceName), out var mapping))
            {
                ApplyMapping(request, mapping, preferenceValue);
            }
        }
    }

    private static PropertyMapping[] GetMappings(Type requestType) => _mappingCache.GetOrAdd(requestType, BuildMappings);

    private static PropertyMapping[] BuildMappings(Type requestType)
    {
        var mappings = new List<PropertyMapping>();
        ScanType(requestType, mappings, string.Empty);
        return mappings.ToArray();
    }

    private static void ScanType(Type type, List<PropertyMapping> mappings, string basePath)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = property.GetCustomAttribute<PreferenceAttribute>();
            if (attribute != null)
            {
                var fullPath = string.IsNullOrEmpty(basePath) ? property.Name : $"{basePath}.{property.Name}";
                mappings.Add(new PropertyMapping
                {
                    PreferenceName = attribute.PreferenceName,
                    NormalizedPreferenceName = NameNormalizer.Normalize(attribute.PreferenceName),
                    PropertyPath = attribute.PropertyPath ?? fullPath,
                    PropertyType = property.PropertyType
                });
            }
            else if (ReflectionHelper.IsComplexType(property.PropertyType))
            {
                var nestedPath = string.IsNullOrEmpty(basePath) ? property.Name : $"{basePath}.{property.Name}";
                ScanType(property.PropertyType, mappings, nestedPath);
            }
        }
    }

    private static void ApplyMapping(object request, PropertyMapping mapping, object preferenceValue)
    {
        var pathParts = mapping.PropertyPath.Split('.');
        var currentObject = request;

        for (var i = 0; i < pathParts.Length - 1; i++)
        {
            var property = currentObject?.GetType().GetProperty(pathParts[i]);
            if (property == null) return;

            var value = property.GetValue(currentObject);
            if (value == null)
            {
                if (property.PropertyType.GetConstructor(Type.EmptyTypes) != null)
                {
                    value = Activator.CreateInstance(property.PropertyType);
                    property.SetValue(currentObject, value);
                }
                else
                {
                    return;
                }
            }
            currentObject = value;
        }

        var finalProperty = currentObject?.GetType().GetProperty(pathParts[^1]);
        if (finalProperty?.CanWrite != true) return;

        var currentValue = finalProperty.GetValue(currentObject);
        if (ReflectionHelper.HasValue(currentValue, finalProperty.PropertyType)) return;

        var convertedValue = ReflectionHelper.ConvertValue(preferenceValue, finalProperty.PropertyType);
        if (convertedValue != null)
        {
            finalProperty.SetValue(currentObject, convertedValue);
        }
    }

    private sealed class PropertyMapping
    {
        public string PreferenceName { get; set; } = string.Empty;
        public string NormalizedPreferenceName { get; set; } = string.Empty;
        public string PropertyPath { get; set; } = string.Empty;
        public Type PropertyType { get; set; } = typeof(object);
    }
}
