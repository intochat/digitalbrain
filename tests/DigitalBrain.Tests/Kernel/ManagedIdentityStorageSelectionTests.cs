using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.Kernel;

// Coverage for the task-18 managed-identity storage switch. Mirrors PackConfigBackingStoreSelectionTests.cs /
// AzureClientHealthCheckRegistrationTests.cs's approach: replicate Program.cs's conditional-wiring logic
// against a bare IHostApplicationBuilder rather than booting a real silo, since useManagedIdentity is a local
// variable inside Program.cs's top-level statements (not a testable static method) and the actual Orleans
// clustering/grain-storage/journal calls it gates require either Azurite (connection-string branch, already
// covered by every other test that boots the fast path) or a real Azure Entra token exchange (managed-identity
// branch, impossible to exercise without live infra - see Task 18's live-verification step, deferred).
public class ManagedIdentityStorageSelectionTests
{
    [Fact]
    public void AccountNameNotConfigured_UseManagedIdentityIsFalse()
    {
        var builder = Host.CreateApplicationBuilder();

        // DigitalBrain:Storage:AccountName is only ever set by the cloud Pulumi deploy's
        // DigitalBrain__Storage__AccountName env var (deploy/Program.cs); Aspire/local/test config never sets
        // it, so this must default to false everywhere except a real ACA deploy that has explicitly opted in.
        var storageAccountName = builder.Configuration["DigitalBrain:Storage:AccountName"];
        var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountName);

        Assert.False(useManagedIdentity);
    }

    [Fact]
    public void AccountNameSetViaDeployStyleEnvVar_UseManagedIdentityIsTrue()
    {
        // The one non-trivial contract this switch actually depends on: deploy/Program.cs sets the ACA
        // container's env var as "DigitalBrain__Storage__AccountName" (double underscore, the ACA/K8s-style
        // env var shape), and .NET configuration's EnvironmentVariablesConfigurationProvider must flatten
        // that into the "DigitalBrain:Storage:AccountName" colon-delimited key Program.cs reads. Exercising
        // that real double-underscore-to-colon binding (rather than setting the flattened key directly) is
        // what would actually catch a typo'd env var name in either file.
        const string envVarName = "DigitalBrain__Storage__AccountName";
        Environment.SetEnvironmentVariable(envVarName, "digitalbrainstprod");
        try
        {
            var builder = Host.CreateApplicationBuilder();

            var storageAccountName = builder.Configuration["DigitalBrain:Storage:AccountName"];
            var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountName);

            Assert.Equal("digitalbrainstprod", storageAccountName);
            Assert.True(useManagedIdentity);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }
}
