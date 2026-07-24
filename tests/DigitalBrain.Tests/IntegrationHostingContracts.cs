using System.Reflection;
using System.Xml.Linq;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class IntegrationHostingContracts
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact(DisplayName = "TestingAppHost project resources are solution members so Release CI builds them")]
    public void TestingAppHostProjectResourcesAreSolutionMembers()
    {
        var testingAppHost = XDocument.Load(
            Path.Combine(RepositoryRoot, "hosts", "DigitalBrain.TestingAppHost", "DigitalBrain.TestingAppHost.csproj"));
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot, "DigitalBrain.slnx"));
        var testingAppHostDirectory = Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.TestingAppHost");
        var projectRefs = testingAppHost
            .Descendants("ProjectReference")
            .Where(reference =>
            {
                var aspireResource = reference.Attribute("IsAspireProjectResource");
                return aspireResource is null
                    || !string.Equals(aspireResource.Value, "false", StringComparison.OrdinalIgnoreCase);
            })
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include =>
            {
                var normalized = include!.Replace('\\', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(testingAppHostDirectory, normalized));
                return Path.GetRelativePath(RepositoryRoot, fullPath).Replace('\\', '/');
            })
            .ToList();

        Assert.Contains(projectRefs, path => path.EndsWith("DigitalBrain.Host.csproj", StringComparison.Ordinal));
        Assert.Contains(projectRefs, path => path.EndsWith("DigitalBrain.ProbeHost.csproj", StringComparison.Ordinal));
        Assert.All(
            projectRefs,
            path => Assert.Contains(path, solution, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "the AppHost build exclusively initializes or synchronizes CodeGraph")]
    public void AppHostBuildOwnsCodeGraphRefresh()
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.targets"));
        var target = Assert.Single(
            document.Descendants("Target"),
            candidate => candidate
                .Descendants("Exec")
                .Any(exec => CommandOf(exec).Contains("@colbymchenry/codegraph", StringComparison.Ordinal)));
        var executions = target.Descendants("Exec").ToArray();

        Assert.Equal("Build", AttributeOf(target, "BeforeTargets"));
        Assert.Equal(
            "'$(IsAspireHost)' == 'true' And '$(MSBuildProjectFullPath)' == '$(_CodeGraphAppHostProject)'",
            AttributeOf(target, "Condition").Trim());
        Assert.Equal(2, executions.Length);

        var initialize = Assert.Single(executions, exec =>
            CommandOf(exec).Contains(" init ", StringComparison.Ordinal));
        var synchronize = Assert.Single(executions, exec =>
            CommandOf(exec).Contains(" sync ", StringComparison.Ordinal));
        var snapshotGroup = Assert.Single(target.Elements("PropertyGroup"));
        var databaseExists = Assert.Single(snapshotGroup.Elements("_CodeGraphDatabaseExists")).Value;

        Assert.Empty(snapshotGroup.ElementsBeforeSelf("Exec"));
        Assert.Equal("$([System.IO.File]::Exists('$(_CodeGraphDatabase)'))", databaseExists);
        Assert.Equal("'$(_CodeGraphDatabaseExists)' != 'True'", AttributeOf(initialize, "Condition"));
        Assert.Equal("'$(_CodeGraphDatabaseExists)' == 'True'", AttributeOf(synchronize, "Condition"));
        Assert.All(executions, exec =>
        {
            var command = CommandOf(exec);

            Assert.StartsWith("npx -y @colbymchenry/codegraph@latest ", command, StringComparison.Ordinal);
            Assert.EndsWith(" \"$(_CodeGraphRepositoryRoot)\"", command, StringComparison.Ordinal);
            Assert.Null(exec.Attribute("ContinueOnError"));
        });

        var repositoryRoot = Assert.Single(document.Descendants("_CodeGraphRepositoryRoot")).Value;
        var appHostProject = Assert.Single(document.Descendants("_CodeGraphAppHostProject")).Value;
        var database = Assert.Single(document.Descendants("_CodeGraphDatabase")).Value;
        var serialized = document.ToString();

        Assert.Contains("NormalizePath", repositoryRoot, StringComparison.Ordinal);
        Assert.Contains("TrimEndingDirectorySeparator", repositoryRoot, StringComparison.Ordinal);
        Assert.Contains("MSBuildThisFileDirectory", repositoryRoot, StringComparison.Ordinal);
        Assert.Contains("_CodeGraphRepositoryRoot", appHostProject, StringComparison.Ordinal);
        Assert.Contains("DigitalBrain.AppHost.csproj", appHostProject, StringComparison.Ordinal);
        Assert.Contains("_CodeGraphRepositoryRoot", database, StringComparison.Ordinal);
        Assert.Contains(".codegraph", database, StringComparison.Ordinal);
        Assert.Contains("codegraph.db", database, StringComparison.Ordinal);
        Assert.DoesNotContain(".codegraph-initialized", serialized, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "AppHost exposes one opaque DigitalBrain root creation surface")]
    public void AppHostExposesOnlyTypedDigitalBrainCreation()
    {
        var add = typeof(DigitalBrainHostingExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "AddDigitalBrain");

        Assert.Equal(typeof(IDistributedApplicationBuilder), add.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(string), add.GetParameters()[1].ParameterType);
        Assert.Equal(typeof(DigitalBrainBuilder), add.ReturnType);
        Assert.Equal(
            [nameof(DigitalBrainBuilder.Name)],
            typeof(DigitalBrainBuilder)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name));

        var exported = typeof(DigitalBrainHostingExtensions).Assembly.GetExportedTypes();
        var publicNames = exported
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("AddBrain", publicNames);
        Assert.DoesNotContain("WithAzureStorage", publicNames);
        Assert.DoesNotContain("WithDevelopmentStores", publicNames);
        Assert.DoesNotContain("BrainModuleHosting", exported.Select(type => type.Name));
    }

    [Fact(DisplayName = "integration hosting extensions receive their exact typed module builders")]
    public void IntegrationHostingExtensionsUseTypedModuleReceivers()
    {
        var gmailReceiver = Assert.Single(
            typeof(GoogleHostingExtensions)
                .GetMethod(nameof(GoogleHostingExtensions.WithGmail))!
                .GetParameters());
        var salesforceReceiver = Assert.Single(
            typeof(SalesforceHostingExtensions)
                .GetMethod(nameof(SalesforceHostingExtensions.WithSalesforce))!
                .GetParameters());

        Assert.Equal(typeof(DigitalBrainModuleBuilder<GoogleModule>), gmailReceiver.ParameterType);
        Assert.Equal(typeof(DigitalBrainModuleBuilder<SalesforceModule>), salesforceReceiver.ParameterType);
    }

    [Fact(DisplayName = "module and provider hosting source contains no process-static marker lookup")]
    public async Task ModuleHostingSourceContainsNoConditionalWeakTable()
    {
        var moduleRoots = Directory
            .EnumerateDirectories(
                Path.Combine(RepositoryRoot, "modules"),
                "*.Aspire.Hosting",
                SearchOption.TopDirectoryOnly);
        var integrationRoots = Directory
            .EnumerateDirectories(
                Path.Combine(RepositoryRoot, "src"),
                "*.Aspire.Hosting",
                SearchOption.TopDirectoryOnly);
        var roots = moduleRoots.Concat(integrationRoots);
        var sourceFiles = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var offenders = new List<string>();

        Assert.NotEmpty(sourceFiles);

        foreach (var path in sourceFiles)
        {
            var source = await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken);
            if (source.Contains("ConditionalWeakTable", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'));
            }
        }

        Assert.Empty(offenders);
    }

    [Fact(DisplayName = "AddDigitalBrain owns one complete durable profile")]
    public async Task AddDigitalBrainOwnsOneCompleteDurableProfile()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("orders");

        brain.AddModule<GoogleModule>(google => google.WithGmail());

        var silo = builder.AddResource(new ProjectionProbe("silo")).WithReference(brain);
        var client = builder.AddResource(new ProjectionProbe("client")).WithReference(brain.AsClient());
        var storage = Assert.IsType<AzureStorageResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "orders-storage"));
        var clustering = Assert.IsType<AzureTableStorageResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "orders-clustering"));
        var reminders = Assert.IsType<AzureTableStorageResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "orders-reminders"));
        var journal = Assert.IsType<AzureBlobStorageResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "orders-journal"));
        var protectionKey = Parameter(builder, "orders-state-protection-key");
        var siloEnvironment = await ProjectAsync(silo.Resource);
        var clientEnvironment = await ProjectAsync(client.Resource);
        var siloWaits = silo.Resource.Annotations.OfType<WaitAnnotation>().ToList();
        var durableResources = new IResource[] { storage, clustering, reminders, journal };

        Assert.True(storage.IsEmulator);
        Assert.Same(storage, clustering.Parent);
        Assert.Same(storage, reminders.Parent);
        Assert.Same(storage, journal.Parent);
        Assert.True(protectionKey.Secret);
        Assert.Single(builder.Resources, resource => resource.Name == "orders-state-protection-key");
        Assert.Same(
            protectionKey,
            siloEnvironment["DigitalBrain__Security__StateProtectionKey"]);
        Assert.Contains("ConnectionStrings__orders-clustering", siloEnvironment.Keys);
        Assert.Contains("ConnectionStrings__orders-reminders", siloEnvironment.Keys);
        Assert.Contains("ConnectionStrings__journal", siloEnvironment.Keys);
        Assert.Contains("ConnectionStrings__orders-clustering", clientEnvironment.Keys);
        Assert.DoesNotContain("DigitalBrain__Security__StateProtectionKey", clientEnvironment.Keys);
        Assert.DoesNotContain("ConnectionStrings__orders-reminders", clientEnvironment.Keys);
        Assert.DoesNotContain("ConnectionStrings__journal", clientEnvironment.Keys);
        Assert.All(
            durableResources,
            resource => Assert.Contains(
                siloWaits,
                wait => ReferenceEquals(wait.Resource, resource)
                    && wait.WaitType == WaitType.WaitUntilHealthy));
        Assert.DoesNotContain(
            client.Resource.Annotations.OfType<WaitAnnotation>(),
            wait => durableResources.Contains(wait.Resource));

        var hostingSource = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot,
            "src",
            "DigitalBrain.Aspire.Hosting",
            "DigitalBrainHostingExtensions.cs"),
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain("WithDevelopmentClustering", hostingSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WithMemoryGrainStorage", hostingSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WithMemoryReminders", hostingSource, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "AppHost prompts only for provider-required OAuth parameters")]
    public void AppHostPromptsOnceForOfficialOAuthParameters()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        brain.AddModule<GoogleModule>(google => google.WithGmail());
        brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

        AssertParameter(builder, "google-client-id", secret: false);
        AssertParameter(builder, "google-client-secret", secret: true);
        AssertParameter(builder, "google-redirect-uri", secret: false);
        AssertParameter(builder, "salesforce-client-id", secret: false);
        AssertParameter(builder, "salesforce-redirect-uri", secret: false);
        AssertParameter(builder, "mcp-authorization-mode", secret: false);
        AssertParameter(builder, "brain-state-protection-key", secret: true);
        Assert.DoesNotContain(builder.Resources, resource => resource.Name == "salesforce-client-secret");
        Assert.Single(builder.Resources, resource => resource.Name == "brain-state-protection-key");
    }

    [Fact(DisplayName = "multiple brains share application-scoped OAuth parameters")]
    public void MultipleBrainsShareApplicationScopedOAuthParameters()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddDigitalBrain("first")
            .AddModule<GoogleModule>(google => google.WithGmail());
        builder.AddDigitalBrain("second")
            .AddModule<GoogleModule>(google => google.WithGmail());

        Assert.Single(builder.Resources, resource => resource.Name == "mcp-authorization-mode");
        Assert.Single(builder.Resources, resource => resource.Name == "google-client-id");
        Assert.Single(builder.Resources, resource => resource.Name == "google-client-secret");
        Assert.Single(builder.Resources, resource => resource.Name == "google-redirect-uri");
    }

    [Fact(DisplayName = "integration OAuth configuration is projected only to the silo")]
    public async Task IntegrationOAuthConfigurationIsProjectedOnlyToTheSilo()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        brain.AddModule<GoogleModule>(google => google.WithGmail());
        brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

        var silo = builder.AddResource(new ProjectionProbe("silo")).WithReference(brain);
        var client = builder.AddResource(new ProjectionProbe("client")).WithReference(brain.AsClient());
        var siloEnvironment = await ProjectAsync(silo.Resource);
        var clientEnvironment = await ProjectAsync(client.Resource);

        Assert.Same(
            Parameter(builder, "google-client-secret"),
            siloEnvironment["DigitalBrain__Google__Gmail__ClientSecret"]);
        Assert.Contains("DigitalBrain__Google__Gmail__ClientId", siloEnvironment.Keys);
        Assert.Contains("DigitalBrain__Google__Gmail__RedirectUri", siloEnvironment.Keys);
        Assert.Contains("DigitalBrain__Salesforce__ClientId", siloEnvironment.Keys);
        Assert.Contains("DigitalBrain__Salesforce__RedirectUri", siloEnvironment.Keys);
        Assert.DoesNotContain("DigitalBrain__Salesforce__ClientSecret", siloEnvironment.Keys);
        Assert.Same(
            Parameter(builder, "brain-state-protection-key"),
            siloEnvironment["DigitalBrain__Security__StateProtectionKey"]);
        Assert.Same(
            Parameter(builder, "mcp-authorization-mode"),
            siloEnvironment["DigitalBrain__Integrations__Mcp__AuthorizationMode"]);
        Assert.DoesNotContain(
            clientEnvironment.Keys,
            key => key.StartsWith("DigitalBrain__", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "AppHost rejects duplicate Gmail and Salesforce selections")]
    public void AppHostRejectsDuplicateIntegrationSelections()
    {
        var googleBuilder = DistributedApplication.CreateBuilder();
        var googleBrain = googleBuilder.AddDigitalBrain("google-brain");
        var salesforceBuilder = DistributedApplication.CreateBuilder();
        var salesforceBrain = salesforceBuilder.AddDigitalBrain("salesforce-brain");

        Assert.Throws<InvalidOperationException>(
            () => googleBrain.AddModule<GoogleModule>(google => google
                .WithGmail()
                .WithGmail()));
        Assert.Throws<InvalidOperationException>(
            () => salesforceBrain.AddModule<SalesforceModule>(salesforce => salesforce
                .WithSalesforce()
                .WithSalesforce()));
    }

    private static void AssertParameter(
        IDistributedApplicationBuilder builder,
        string name,
        bool secret)
    {
        var parameter = Parameter(builder, name);

        Assert.Equal(secret, parameter.Secret);
        Assert.False(string.IsNullOrWhiteSpace(parameter.Description));
    }

    private static ParameterResource Parameter(IDistributedApplicationBuilder builder, string name)
        => Assert.IsType<ParameterResource>(
            Assert.Single(builder.Resources, resource => resource.Name == name));

    private static string AttributeOf(XElement element, string name) =>
        element.Attribute(name)?.Value
        ?? throw new InvalidOperationException($"{element.Name.LocalName} carries no {name} attribute.");

    private static string CommandOf(XElement exec) => AttributeOf(exec, "Command");

    private static bool IsBuildOutput(string path)
        => path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
            || path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase);

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("DigitalBrain.slnx was not found above the test assembly.");
    }

    private static async Task<Dictionary<string, object>> ProjectAsync(IResourceWithEnvironment resource)
    {
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish));

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
    }

    private sealed class ProjectionProbe(string name) : Resource(name), IResourceWithEnvironment, IResourceWithEndpoints;
}
