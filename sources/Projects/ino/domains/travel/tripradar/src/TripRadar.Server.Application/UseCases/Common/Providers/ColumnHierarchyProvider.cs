using TripRadar.Server.Application.Contracts.Services.Providers;

namespace TripRadar.Server.Application.UseCases.Common.Providers;

public abstract class ColumnHierarchyProvider : IColumnHierarchyProvider
{
    protected abstract Dictionary<string, string?> ColumnHierarchies { get; }
    protected abstract HashSet<string?> ValidColumns { get; }

    public string?[] GetRootColumn(string columnName) => [ColumnHierarchies.GetValueOrDefault(columnName.ToLowerInvariant())];

    public bool IsValidColumn(string columnName) => ValidColumns.Contains(columnName.ToLowerInvariant());
}
