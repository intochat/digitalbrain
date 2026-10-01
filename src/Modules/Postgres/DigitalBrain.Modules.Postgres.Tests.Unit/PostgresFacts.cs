using DigitalBrain.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using Orleans.Hosting;

namespace DigitalBrain.Modules.Postgres.Tests.Unit;

public sealed class PostgresFacts
{
    [Fact]
    public async Task QueryReturnsTypedRowsThroughTheDefaultGrainType()
    {
        await using var brain = await StartAsync();
        var result = await brain.Get<IPostgres>("default").Query(new(" select id from people; ", 10));
        Assert.Equal(1, result.RowCount);
        Assert.False(result.Truncated);
        Assert.Equal("id", Assert.Single(result.Columns).Name);
        Assert.Equal("7", Assert.Single(Assert.Single(result.Rows)));
    }

    [Fact]
    public async Task InvalidQueriesAreRejectedBeforeDatabaseAccess()
    {
        await using var brain = await StartAsync();
        var postgres = brain.Get<IPostgres>("default");
        await Assert.ThrowsAsync<PostgresQueryException>(() => postgres.Query(new(" ")));
        await Assert.ThrowsAsync<PostgresQueryException>(() => postgres.Query(new("delete from people")));
        await Assert.ThrowsAsync<PostgresQueryException>(() => postgres.Query(new("select 1", 0)));
        await Assert.ThrowsAsync<PostgresQueryException>(() => postgres.Query(new("select 1", 1001)));
    }

    [Fact]
    public async Task SchemaAndConnectionAreAvailableThroughTheNeuron()
    {
        await using var brain = await StartAsync("reporting");
        var postgres = brain.Get<IPostgres>("default");
        var schema = await postgres.ReadSchema(new(" reporting.people "));
        Assert.Equal("reporting.people", Assert.Single(schema.Tables).Name);
        var connection = await postgres.ReadConnection();
        Assert.True(connection.Connected);
        Assert.Equal("sample", connection.Database);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostingKeepsThePostgresPoolSeparateFromOtherModules(bool postgresFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Modules:PostgresModule:Options"] = """{"ConnectionName":"analytics"}""",
                ["ConnectionStrings:analytics"] = "Host=localhost;Database=analytics;Username=reader",
                ["ConnectionStrings:supabase"] = "Host=localhost;Database=supabase;Username=reader",
            }).Build();
        services.AddSingleton<IConfiguration>(configuration);
        var silo = new PostgresTestSiloBuilder(services, configuration);
        if (postgresFirst) { silo.AddPostgres(); }
        new DigitalBrain.Supabase.SupabaseModule().Configure(silo);
        silo.AddPostgres();
        silo.AddPostgres();
        await using var provider = services.BuildServiceProvider();
        Assert.Equal("supabase", new NpgsqlConnectionStringBuilder(provider.GetRequiredService<NpgsqlDataSource>().ConnectionString).Database);
        var source = provider.GetRequiredKeyedService<NpgsqlDataSource>(PostgresHosting.DataSourceKey);
        Assert.Equal("analytics", new NpgsqlConnectionStringBuilder(source.ConnectionString).Database);
        Assert.Single(provider.GetServices<IPostgresProvider>());
        Assert.Single(provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations, registration => registration.Name == "postgres");
    }

    [Theory]
    [InlineData("postgresql://reader:p%40ss@localhost:5433/sample?sslmode=require")]
    [InlineData("Host=localhost;Port=5433;Database=sample;Username=reader;Password=p@ss")]
    public void ConnectionFormatsAreParsedAndSensitiveDiagnosticsAreDisabled(string connection)
    {
        var settings = PostgresConnectionSettings.Parse(connection);
        Assert.Equal("sample", settings.Database);
        Assert.Equal("reader", settings.Username);
        Assert.Equal("p@ss", settings.Password);
        Assert.Equal(5433, settings.Port);
        Assert.False(settings.IncludeErrorDetail);
        Assert.False(settings.LogParameters);
        Assert.False(settings.PersistSecurityInfo);
    }

    [Fact]
    public void InvalidConnectionDoesNotLeakCredentials()
    {
        var error = Assert.Throws<InvalidOperationException>(() => PostgresConnectionSettings.Parse("Password=secret-value"));
        Assert.DoesNotContain("secret-value", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Password=secret-value")]
    public async Task StartupRejectsMissingOrInvalidConnections(string? connection)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:postgres"] = connection,
        }).Build();
        services.AddSingleton<IConfiguration>(configuration);
        new PostgresTestSiloBuilder(services, configuration).AddPostgres();
        using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
            await provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("secret-value", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "text", "null")]
    [InlineData("t", "boolean", "true")]
    [InlineData("f", "boolean", "false")]
    [InlineData("42", "number", "42")]
    [InlineData("9007199254740993", "number", "\"9007199254740993\"")]
    [InlineData("1.234567890123456789", "number", "\"1.234567890123456789\"")]
    [InlineData("NaN", "number", "\"NaN\"")]
    public void CellsPreserveNullsBooleansAndHighPrecisionValues(string? value, string type, string expected)
        => Assert.Equal(expected, PostgresCells.ToCell(value, type));

    [Fact]
    public async Task InvalidSchemaNameIsRejected()
    {
        await using var brain = await StartAsync();
        await Assert.ThrowsAsync<PostgresQueryException>(() => brain.Get<IPostgres>("default").ReadSchema(new("people; drop table people")));
    }

    [Fact]
    public async Task HealthCheckResolvesWithValidatedServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader",
        }).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IPostgresProvider, PostgresTestControls>();
        await using var brain = await StartAsync();
        services.AddSingleton<DigitalBrain.Contracts.IDigitalBrain>(brain);
        new PostgresTestSiloBuilder(services, configuration).AddPostgres();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HealthStatus.Healthy, report.Entries["postgres"].Status);
    }

    private static Task<UnitBrain> StartAsync(string connectionName = "postgres")
        => UnitTest.Create().WithModule<PostgresModule, PostgresModuleOptions>(module => module.WithConnection(connectionName))
            .ConfigureSilo(silo =>
            {
                silo.Configuration[$"ConnectionStrings:{connectionName}"] = "Host=localhost;Database=sample;Username=reader";
                silo.Services.AddSingleton<IPostgresProvider, PostgresTestControls>();
            }).StartAsync(TestContext.Current.CancellationToken);
}
