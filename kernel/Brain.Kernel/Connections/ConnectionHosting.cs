using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

namespace Brain.Kernel.Connections;

public static class ConnectionHosting
{
    public static ISiloBuilder AddBrainConnection(this ISiloBuilder silo)
    {
        var options = new ConnectionSecurityOptions
        {
            KeyRingPath = silo.Configuration[$"{ConnectionSecurityOptions.SectionName}:KeyRingPath"],
            KeyRingBlobUri = silo.Configuration[$"{ConnectionSecurityOptions.SectionName}:KeyRingBlobUri"]
        };
        var environmentName = silo.Configuration["DOTNET_ENVIRONMENT"]
            ?? silo.Configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environments.Development;
        var dataProtection = silo.Services
            .AddDataProtection()
            .SetApplicationName("DigitalBrain");

        if (string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.KeyRingBlobUri))
                throw new InvalidOperationException(
                    $"{ConnectionSecurityOptions.SectionName}:KeyRingBlobUri is required in Production.");

            dataProtection.PersistKeysToAzureBlobStorage(new Uri(options.KeyRingBlobUri, UriKind.Absolute));
        }
        else
        {
            var keyRingPath = options.KeyRingPath ?? Path.Combine(RepositoryRoot(), ".digitalbrain", "keys");
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }

        silo.Services.AddSingleton<IConnectionTokenProtector, DataProtectionConnectionTokenProtector>();
        return silo.AddBrainKind("connection", sp => new ConnectionKind(sp));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            directory = directory.Parent;

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }
}
