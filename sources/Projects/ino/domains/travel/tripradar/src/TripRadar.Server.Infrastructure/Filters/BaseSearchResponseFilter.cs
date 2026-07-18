using Microsoft.Extensions.Logging;
using TripRadar.Server.Domain.ValueObjects;
using TripRadar.Server.Infrastructure.Contracts;

namespace TripRadar.Server.Infrastructure.Filters;

public abstract class BaseSearchResponseFilter<TResponse>(ILogger<BaseSearchResponseFilter<TResponse>> logger)
    : ISearchResponseFilter<TResponse>
{
    public TResponse Filter(TResponse response, IList<QueryColumn>? selectedColumns)
    {
        if (response == null)
        {
            logger.LogWarning("Attempted to filter null response");
            return response!;
        }

        if (selectedColumns == null || !selectedColumns.Any())
        {
            return response;
        }

        var activeColumns = selectedColumns
            .Where(c => c.IsActive)
            .Select(c => c.Name.ToLower())
            .ToList();

        return activeColumns.Count == 0 ? response : FilterResponse(response, activeColumns);
    }

    /// <summary>
    ///     Implements the specific filtering logic for the response type
    /// </summary>
    /// <param name="response">The original response to filter</param>
    /// <param name="activeColumns">List of active column names in lowercase</param>
    /// <returns>The filtered response</returns>
    protected abstract TResponse FilterResponse(TResponse response, List<string> activeColumns);

    /// <summary>
    ///     Checks if a column is active in the list of active columns
    /// </summary>
    /// <param name="columnName">The name of the column to check</param>
    /// <param name="activeColumns">List of active column names</param>
    /// <returns>True if the column is active, false otherwise</returns>
    protected static bool IsColumnActive(string columnName, List<string> activeColumns) => activeColumns.Contains(columnName.ToLower());

    /// <summary>
    ///     Creates a new instance of a type with only the specified properties
    /// </summary>
    /// <typeparam name="T">The type to create</typeparam>
    /// <param name="source">The source object to copy properties from</param>
    /// <param name="activeColumns">List of active column names</param>
    /// <param name="propertyMappings">Dictionary mapping property names to column names</param>
    /// <returns>A new instance with only the active properties</returns>
    protected static T CreateFilteredInstance<T>(T source, List<string> activeColumns, Dictionary<string, string> propertyMappings) where T : new()
    {
        var result = new T();
        var properties = typeof(T).GetProperties();

        foreach (var property in properties)
            if (propertyMappings.TryGetValue(property.Name, out var columnName) &&
                IsColumnActive(columnName, activeColumns)) property.SetValue(result, property.GetValue(source));

        return result;
    }
}
