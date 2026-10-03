namespace DigitalBrain.Postgres.Aspire.Hosting;

internal sealed class PostgresHostingOptions
{
    public string ConnectionName { get; set; } = "postgres";
    public string DatabaseName { get; set; } = "postgres";
    public bool PersistentStorage { get; set; } = true;
}
