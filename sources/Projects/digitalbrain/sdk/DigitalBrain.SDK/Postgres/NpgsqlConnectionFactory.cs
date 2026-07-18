using Npgsql;

namespace DigitalBrain.SDK.Postgres;

public sealed class NpgsqlConnectionFactory(
    IServiceProvider serviceProvider,
    IConfiguration configuration) : INpgsqlConnectionFactory
{
    public async Task<NpgsqlConnection> OpenConnectionAsync(string databaseId, CancellationToken cancellationToken = default)
    {
        // Resolve named databases dynamically via Orleans Keyed DI first
        var dataSource = serviceProvider.GetKeyedService<NpgsqlDataSource>(databaseId);
        if (dataSource != null)
        {
            var connection = dataSource.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        // Fallback to connection string configuration
        var connectionString = configuration[$"DigitalBrain:Data:Postgres:Connections:{databaseId}"] 
            ?? configuration["DigitalBrain:Data:Postgres:ConnectionString"]
            ?? "Host=localhost;Database=" + databaseId + ";Username=postgres;Password=postgres";

        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);
        return conn;
    }
}
