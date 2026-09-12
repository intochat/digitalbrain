namespace DigitalBrain.ClickHouse;

// The module's vocabulary: grain types, the id prefix that routes a table to this module,
// and the Aspire resource names the hosting projection creates.
public static class ClickHouseNames
{
    public const string NeuronType = "clickhouse";
    public const string DefaultNeuron = "default";
    public const string TableType = "clickhouse-table";
    public const string TableIdPrefix = "chtable-";

    public const string Server = "clickhouse";
    public const string DatabaseResource = "clickhouse-db";
    public const string DatabaseName = "digitalbrain";
}
