namespace DigitalBrain.Capabilities.Connections.Catalog;

using DigitalBrain.Capabilities.Connections.Requests;

/// <summary>An entry in the connection catalog projection.</summary>
public sealed record CatalogRow(
    string Key,
    string ConnectorId,
    string ConnectionId,
    ConnectionStatus Status,
    string? Account,
    DateTimeOffset? LastProbedAt);

/// <summary>CQRS projection state of connection catalog and requests.</summary>
public sealed record ConnectionCatalog
{
    /// <summary>Probed and configured connection rows.</summary>
    public CatalogRow[] Rows { get; init; } = [];

    /// <summary>Pending requested connection references.</summary>
    public ConnectionRef[] Pending { get; init; } = [];

    /// <summary>Catalog revision counter.</summary>
    public long Revision { get; init; }

    /// <summary>Explicit user-specified ordering of connection keys.</summary>
    public string[] Order { get; init; } = [];

    /// <summary>Finds a catalog row by connection key.</summary>
    public CatalogRow? RowOf(string key)
        => Rows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));

    /// <summary>Adds or updates a catalog row, returning the same instance if unchanged.</summary>
    public ConnectionCatalog Put(CatalogRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var existingIndex = Array.FindIndex(Rows, r => string.Equals(r.Key, row.Key, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            var existing = Rows[existingIndex];
            if (existing == row)
            {
                return this;
            }

            var updated = (CatalogRow[])Rows.Clone();
            updated[existingIndex] = row;
            return this with { Rows = updated };
        }

        return this with { Rows = [.. Rows, row] };
    }

    /// <summary>Removes a catalog row by connection key, returning the same instance if not found.</summary>
    public ConnectionCatalog Without(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var filtered = Rows.Where(r => !string.Equals(r.Key, key, StringComparison.Ordinal)).ToArray();
        return filtered.Length == Rows.Length ? this : this with { Rows = filtered };
    }

    /// <summary>Checks whether a connector has at least one active (non-disconnected) connection.</summary>
    public bool Resolved(string connectorId)
        => Rows.Any(r => string.Equals(r.ConnectorId, connectorId, StringComparison.Ordinal) && r.Status != ConnectionStatus.Disconnected);
}
