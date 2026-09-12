namespace DigitalBrain.UI;

// Routes a table id to the grain type that serves it. The UI module owns "table-"; another
// module registers its own prefix and grain type so TableService, the endpoints and the agent
// tools work on its tables unchanged.
public interface ITableSource
{
    string IdPrefix { get; }
    string GrainType { get; }
}

public sealed record TableSource(string IdPrefix, string GrainType) : ITableSource;
