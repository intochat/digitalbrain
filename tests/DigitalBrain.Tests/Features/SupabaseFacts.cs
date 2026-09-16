using Xunit;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Supabase;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Tests;

public sealed class SupabaseFacts
{
    [Fact]
    public void Supabase_module_is_available_to_the_silo()
    {
        Assert.NotNull(Type.GetType("DigitalBrain.Supabase.SupabaseModule, DigitalBrain.Modules.Supabase"));
    }

    [Theory]
    [InlineData("INSERT INTO things VALUES (1)")]
    [InlineData("WITH deleted AS (DELETE FROM things RETURNING *) SELECT * FROM deleted")]
    [InlineData("SELECT 1; DELETE FROM things")]
    [InlineData("SELECT * INTO copy FROM things")]
    [InlineData("SELECT set_config('transaction_read_only', 'off', true)")]
    [InlineData("SELECT pg_advisory_lock(1)")]
    [InlineData("SELECT nextval('sequence')")]
    [InlineData("SELECT public.\"set_config\"('statement_timeout', '0', false)")]
    [InlineData("SELECT 1 -- comment")]
    [InlineData("SELECT $$text$$")]
    [InlineData("SELECT E'escaped\\text'")]
    public void Guard_refuses_writes_and_session_control(string sql)
        => Assert.Throws<ArgumentException>(() => SupabaseQueryGuard.Validate(sql));

    [Theory]
    [InlineData("SELECT id, name FROM public.people")]
    [InlineData("WITH people AS (SELECT 1 AS id) SELECT * FROM people")]
    [InlineData("SELECT 'DELETE; -- harmless text' AS label")]
    [InlineData("SELECT 'it''s fine', \"order\" FROM public.people")]
    public void Guard_accepts_read_queries(string sql) => SupabaseQueryGuard.Validate(sql);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parameterless_module_registration_forwards_one_secret_connection(bool configured)
    {
        var builder = DistributedApplication.CreateBuilder([]);
        if (configured)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:supabase"] = "Host=localhost;Database=example;Username=reader;Password=example",
            });
        }

        var brain = builder.AddDigitalBrain("brain").AddModule<SupabaseModule>().AddModule<SupabaseModule>();
        var consumer = builder.AddExecutable("consumer", "unused", ".").WithReference(brain);
        var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>(), value => value.Name == "supabase");
        Assert.True(parameter.Secret);
        Assert.True(parameter.IsConnectionString);
        var environment = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, environment, TestContext.Current.CancellationToken);
        foreach (var callback in consumer.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        var reference = Assert.IsAssignableFrom<IValueProvider>(environment["ConnectionStrings__supabase"]);
        if (configured)
        {
            Assert.Equal("Host=localhost;Database=example;Username=reader;Password=example", await reference.GetValueAsync(TestContext.Current.CancellationToken));
        }
        Assert.Equal("Npgsql", environment["DigitalBrain__Supabase__Provider"]);
        await using var app = builder.Build();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fake_composition_does_not_ask_for_a_connection_string(bool fakesFirst)
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var brain = builder.AddDigitalBrain("brain");
        if (fakesFirst) { brain.WithDigitalBrainFakes(); }
        brain.AddModule<SupabaseModule>();
        if (!fakesFirst) { brain.WithDigitalBrainFakes(); }
        builder.AddExecutable("consumer", "unused", ".").WithReference(brain);
        Assert.DoesNotContain(builder.Resources.OfType<ParameterResource>(), value => value.Name == "supabase");
        await using var app = builder.Build();
    }
}

public sealed class SupabaseTypeFacts
{
    [Theory]
    [InlineData("0.0000000000000000000000000000000000000001")]
    [InlineData("1.234e-28")]
    [InlineData("1e-400")]
    public void Decimal_underflow_is_preserved_as_text(string value)
        => Assert.Equal(value, SupabaseCells.ToCell(value, "number").GetString());

    [Theory]
    [InlineData("int4", "number")]
    [InlineData("numeric", "number")]
    [InlineData("boolean", "boolean")]
    [InlineData("date", "date")]
    [InlineData("timestamp with time zone", "text")]
    [InlineData("jsonb", "text")]
    [InlineData("uuid", "text")]
    public void Maps_Postgres_types_to_table_types(string type, string expected)
        => Assert.Equal(expected, SupabaseTypeMap.ToTableType(type));
}

public sealed class SupabaseConnectionFacts
{
    [Fact]
    public void Accepts_the_connection_URI_copied_from_Supabase()
    {
        var settings = SupabaseConnectionSettings.Parse("postgresql://reader.project:p%40ss%3Bword@db.example.com:5432/postgres?sslmode=require");
        Assert.Equal("db.example.com", settings.Host);
        Assert.Equal("reader.project", settings.Username);
        Assert.Equal("p@ss;word", settings.Password);
        Assert.Equal("postgres", settings.Database);
        Assert.Equal(Npgsql.SslMode.Require, settings.SslMode);
        Assert.False(settings.IncludeErrorDetail);
        Assert.False(settings.LogParameters);
    }

    [Fact]
    public void Accepts_Npgsql_syntax_and_keeps_secrets_out_of_configuration_errors()
    {
        Assert.Equal("reader", SupabaseConnectionSettings.Parse("Host=localhost;Username=reader;Database=postgres").Username);
        var error = Assert.Throws<InvalidOperationException>(() => SupabaseConnectionSettings.Parse("Host=localhost;Password=secret;BadKeyword=secret"));
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }
}
