using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class HostingProjectionContracts
{
    private static readonly string[] SiloOnlyEnvironmentKeys =
    [
        "ConnectionStrings__journal",
        "DigitalBrain__Security__StateProtectionKey",
        "DigitalBrain__Modules__0",
        "DigitalBrain__AI__Ollama__Endpoint",
        "DigitalBrain__AI__Ollama__Llama32__Model",
        "DigitalBrain__Integrations__Mcp__AuthorizationMode",
        "DigitalBrain__Google__Gmail__ClientId",
        "DigitalBrain__Google__Gmail__ClientSecret",
        "DigitalBrain__Google__Gmail__RedirectUri",
        "DigitalBrain__Salesforce__ClientId",
        "DigitalBrain__Salesforce__RedirectUri",
    ];

    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact(DisplayName =
        "WithReference(brain) projects journal, state-protection key, AI config, and OAuth; AsClient never does")]
    public async Task CompleteBrainReferenceProjectsSiloOnlySecretsAndClientDoesNot()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
        brain.AddModule<GoogleModule>(google => google.WithGmail());
        brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

        var silo = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        var client = builder
            .AddContainer("client", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain.AsClient());

        var siloEnvironment = await EnvironmentKeysOf(silo.Resource).ConfigureAwait(true);
        var clientEnvironment = await EnvironmentKeysOf(client.Resource).ConfigureAwait(true);

        Assert.All(SiloOnlyEnvironmentKeys, key => Assert.Contains(key, siloEnvironment));
        Assert.All(SiloOnlyEnvironmentKeys, key => Assert.DoesNotContain(key, clientEnvironment));

        Assert.Contains(
            silo.Resource.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy);
        Assert.DoesNotContain(
            client.Resource.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy);
    }

    [Fact(DisplayName =
        "client WithReference source is only Orleans.AsClient — never journal, protection, modules, or projections")]
    public void ClientWithReferenceSourceNeverTouchesSiloOnlyMaterial()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "DigitalBrain.Aspire.Hosting",
            "DigitalBrainHostingExtensions.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var clientBody = MethodBody(source, "ClientDigitalBrainReference client");
        var siloBody = MethodBody(
            source,
            "this IResourceBuilder<TResource> builder,\n        DigitalBrainBuilder brain)");

        Assert.Contains("client.Brain.Orleans.AsClient()", clientBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Journal", clientBody, StringComparison.Ordinal);
        Assert.DoesNotContain("StateProtectionKey", clientBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Modules", clientBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Projections", clientBody, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupDependencies", clientBody, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitAnnotation", clientBody, StringComparison.Ordinal);
        Assert.DoesNotContain("WithEnvironment", clientBody, StringComparison.Ordinal);
        Assert.DoesNotContain("projection.Apply", clientBody, StringComparison.Ordinal);

        Assert.Contains("brain.Journal", siloBody, StringComparison.Ordinal);
        Assert.Contains("\"journal\"", siloBody, StringComparison.Ordinal);
        Assert.Contains("DigitalBrain__Security__StateProtectionKey", siloBody, StringComparison.Ordinal);
        Assert.Contains("brain.StateProtectionKey", siloBody, StringComparison.Ordinal);
        Assert.Contains("brain.Modules", siloBody, StringComparison.Ordinal);
        Assert.Contains("projection.Apply", siloBody, StringComparison.Ordinal);
        Assert.Contains("WaitAnnotation", siloBody, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "production AppHost silo takes the complete brain; MCP takes AsClient only")]
    public void ProductionAppHostKeepsSiloCompleteAndNorthboundClientsAsClientOnly()
    {
        var appHost = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.AppHost",
            "AppHost.cs"));

        var siloBlock = Between(appHost, "var silo =", ";");
        var mcpBlock = Between(appHost, "builder.AddProject<Projects.DigitalBrain_Mcp>", ";");

        Assert.Contains(".WithReference(brain)", siloBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("AsClient", siloBlock, StringComparison.Ordinal);
        Assert.Contains(".WithReference(brain.AsClient())", mcpBlock, StringComparison.Ordinal);
        Assert.DoesNotContain(".WithReference(brain)", mcpBlock.Replace(
            ".WithReference(brain.AsClient())",
            string.Empty,
            StringComparison.Ordinal), StringComparison.Ordinal);
    }

    private static async Task<HashSet<string>> EnvironmentKeysOf(ContainerResource resource)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var execution = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run);
        var context = new EnvironmentCallbackContext(execution, resource);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context).ConfigureAwait(false);
        }

        keys.UnionWith(context.EnvironmentVariables.Keys);
        return keys;
    }

    private static string MethodBody(string source, string signatureMarker)
    {
        var signatureIndex = source.IndexOf(signatureMarker, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Signature marker '{signatureMarker}' was not found.");

        var openBrace = source.IndexOf('{', signatureIndex);
        Assert.True(openBrace >= 0, $"Opening brace after '{signatureMarker}' was not found.");

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[(openBrace + 1)..index];
                }
            }
        }

        throw new InvalidOperationException($"Could not balance braces for '{signatureMarker}'.");
    }

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker '{start}' was not found.");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"End marker '{end}' after '{start}' was not found.");
        return source[startIndex..endIndex];
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("DigitalBrain.slnx was not found above the test assembly.");
    }
}
