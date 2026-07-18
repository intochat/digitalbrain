using Hangfire.PostgreSql;
using Npgsql;

namespace TripRadar.Server.Infrastructure.Factories;

public class NpgsqlDataSourceConnectionFactory(NpgsqlDataSource dataSource) : IConnectionFactory
{
    public NpgsqlConnection GetOrCreateConnection() => dataSource.CreateConnection();
}
