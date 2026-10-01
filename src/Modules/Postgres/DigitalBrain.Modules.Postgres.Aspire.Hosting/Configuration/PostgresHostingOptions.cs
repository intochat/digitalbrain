namespace DigitalBrain.Postgres.Aspire.Hosting;

internal sealed class PostgresHostingOptions
{
    public string ConnectionName { get; set; } = PostgresModule.ConnectionName;
    public string DatabaseName { get; set; } = "postgres";
    public bool PersistentStorage { get; set; } = true;
}
