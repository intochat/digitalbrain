using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.Kernel;

public class ManagedIdentityStorageSelectionTests
{
    [Fact]
    public void AccountNameNotConfigured_UseManagedIdentityIsFalse()
    {
        var builder = Host.CreateApplicationBuilder();

        var storageAccountName = builder.Configuration["DigitalBrain:Storage:AccountName"];
        var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountName);

        Assert.False(useManagedIdentity);
    }

    [Fact]
    public void AccountNameSetViaDeployStyleEnvVar_UseManagedIdentityIsTrue()
    {

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
