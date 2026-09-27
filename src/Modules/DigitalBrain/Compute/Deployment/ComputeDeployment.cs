using DeploymentKit.Settings;
using DigitalBrain.Deployment;
using Pulumi;

namespace DigitalBrain.Compute;

// The ledger lives in DeploymentKit's PostgreSQL Flexible Server database, administered with the password
// the AppHost declared for the ledger's server; the runtime gets a TLS connection string to it.
public sealed class ComputeDeployment : IDigitalBrainModuleDeployment
{
    public void ConfigureFoundation(FoundationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // DeploymentKit takes the admin password as a plain string when it builds its settings; it returns it as a secret.
        var server = context.Manifest.ServingContainer(context.Manifest.Runtime, ComputeModule.LedgerConnectionName);
        context.Infrastructure.AddDatabase(new DatabaseSettings { AdminPassword = context.Settings.Require(server + "-password") });
    }

    public void Deploy(ModuleDeploymentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var database = context.Foundation.Database
            ?? throw new InvalidOperationException("The compute ledger needs DeploymentKit's database; ConfigureFoundation adds it.");
        var ledgerResources = context.Manifest.ResourcesBehindConnection(context.Runtime, ComputeModule.LedgerConnectionName);
        var server = ledgerResources.First(resource => context.Manifest.TypeOf(resource) == "container.v0");
        var connection = Output.Tuple(database.Host, database.AdminPassword)
            .Apply(parts => $"Host={parts.Item1};Port=5432;Username={database.AdminUser};Password={parts.Item2};Database={database.DatabaseName};SSL Mode=Require");
        foreach (var resource in ledgerResources) { context.Provide(resource, "connectionString", connection, secret: true); }
        context.Provide(server, "bindings.tcp.host", database.Host);
        context.Provide(server, "bindings.tcp.port", "5432");

        // Aspire also writes the local container's user, database and URLs as COMPUTE_*; in the cloud they
        // must describe the Flexible Server instead.
        var prefix = ComputeModule.LedgerConnectionName.ToUpperInvariant() + "_";
        var written = context.Manifest.Environment(context.Runtime);
        void Override(string suffix, Input<string> value, bool secret = false)
        {
            if (written.ContainsKey(prefix + suffix)) { context.SetRuntimeEnvironment(prefix + suffix, value, secret); }
        }
        Override("USERNAME", database.AdminUser);
        Override("DATABASENAME", database.DatabaseName);
        Override("PASSWORD", database.AdminPassword, secret: true);
        Override("URI", Output.Tuple(database.Host, database.AdminPassword).Apply(parts
            => $"postgresql://{database.AdminUser}:{Uri.EscapeDataString(parts.Item2)}@{parts.Item1}:5432/{database.DatabaseName}?sslmode=require"), secret: true);
        Override("JDBCCONNECTIONSTRING", database.Host.Apply(host => $"jdbc:postgresql://{host}:5432/{database.DatabaseName}?sslmode=require"));
    }
}
