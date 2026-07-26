namespace DigitalBrain.Tests.Packages;

internal static class PackageInventory
{
    internal const string Metapackage = "DigitalBrain";
    internal const string Abstractions = "DigitalBrain.Abstractions";
    internal const string Behaviors = "DigitalBrain.Behaviors";
    internal const string Client = "DigitalBrain.Client";
    internal const string Kernel = "DigitalBrain.Kernel";
    internal const string Security = "DigitalBrain.Security";
    internal const string Testing = "DigitalBrain.Testing";
    internal const string Aspire = "DigitalBrain.Aspire";
    internal const string AspireHosting = "DigitalBrain.Aspire.Hosting";
    internal const string IntegrationsMcp = "DigitalBrain.Integrations.Mcp";
    internal const string IntegrationsMcpAspireHosting = "DigitalBrain.Integrations.Mcp.Aspire.Hosting";
    internal const string Ui = "DigitalBrain.Ui";
    internal const string ProductSiloHost = "DigitalBrain.Host";
    internal const string ProductAppHost = "DigitalBrain.AppHost";
    internal const string AccountEnrichment = "DigitalBrain.AccountEnrichment";
    internal const string Quickstart = "DigitalBrain.Quickstart";
    internal const string QuickstartContracts = "DigitalBrain.Quickstart.Contracts";

    internal const string ModulesAi = "DigitalBrain.Modules.AI";
    internal const string ModulesAiContracts = "DigitalBrain.Modules.AI.Contracts";
    internal const string ModulesAiAspireHosting = "DigitalBrain.Modules.AI.Aspire.Hosting";
    internal const string ModulesGoogle = "DigitalBrain.Modules.Google";
    internal const string ModulesGoogleContracts = "DigitalBrain.Modules.Google.Contracts";
    internal const string ModulesGoogleAspireHosting = "DigitalBrain.Modules.Google.Aspire.Hosting";
    internal const string ModulesSalesforce = "DigitalBrain.Modules.Salesforce";
    internal const string ModulesSalesforceContracts = "DigitalBrain.Modules.Salesforce.Contracts";
    internal const string ModulesSalesforceAspireHosting = "DigitalBrain.Modules.Salesforce.Aspire.Hosting";
    internal const string ModulesTasks = "DigitalBrain.Modules.Tasks";
    internal const string ModulesTasksContracts = "DigitalBrain.Modules.Tasks.Contracts";
    internal const string ModulesTime = "DigitalBrain.Modules.Time";
    internal const string ModulesTimeContracts = "DigitalBrain.Modules.Time.Contracts";
    internal const string ModulesFlutter = "DigitalBrain.Modules.Flutter";
    internal const string ModulesFlutterContracts = "DigitalBrain.Modules.Flutter.Contracts";
    internal const string ModulesFlutterAspireHosting = "DigitalBrain.Modules.Flutter.Aspire.Hosting";

    internal const string ModulesPrefix = "DigitalBrain.Modules.";
    internal const string IntegrationsPrefix = "DigitalBrain.Integrations.";
    internal const string AspireFamilyPrefix = "DigitalBrain.Aspire";
    internal const string UiPrefix = "DigitalBrain.Ui.";

    internal static readonly string[] Packable =
    [
        Metapackage,
        Abstractions,
        Behaviors,
        Kernel,
        Client,
        Testing,
        Aspire,
        AspireHosting,
        Security,
        IntegrationsMcp,
        IntegrationsMcpAspireHosting,
        ModulesAiContracts,
        ModulesAi,
        ModulesAiAspireHosting,
        ModulesGoogleContracts,
        ModulesGoogle,
        ModulesGoogleAspireHosting,
        ModulesSalesforceContracts,
        ModulesSalesforce,
        ModulesSalesforceAspireHosting,
        ModulesTasksContracts,
        ModulesTasks,
        ModulesTimeContracts,
        ModulesTime,
        ModulesFlutterContracts,
        ModulesFlutter,
        ModulesFlutterAspireHosting,
        QuickstartContracts,
        Quickstart,
    ];

    internal static readonly string[] AbstractionsDirectPackages = ["Microsoft.Orleans.Sdk"];

    internal static readonly string[] ClientDirectProjects = [Abstractions];

    internal static readonly string[] ClientDirectPackages = ["Microsoft.Orleans.Client"];

    internal static readonly string[] SecurityDirectPackages =
    [
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
    ];

    internal static readonly string[] IntegrationsMcpDirectProjects = [Security];

    internal static readonly string[] IntegrationsMcpDirectPackages =
    [
        "Microsoft.Extensions.Http",
        "Microsoft.Orleans.Journaling",
        "ModelContextProtocol.Core",
    ];

    internal static readonly string[] IntegrationsMcpAspireHostingDirectProjects = [AspireHosting];

    internal static readonly string[] IntegrationsMcpAspireHostingCompileReachable =
    [
        Abstractions,
        AspireHosting,
    ];

    internal static readonly string[] MetapackageDirectProjects =
    [
        Abstractions,
        Aspire,
        Client,
    ];

    internal static readonly string[] AspireDirectProjects = [Client];

    internal static readonly string[] AspireDirectPackages = ["Microsoft.Orleans.Client"];

    internal static readonly string[] AspireCompileReachable =
    [
        Abstractions,
        Client,
    ];

    internal static readonly string[] AspireHostingDirectProjects = [Abstractions];

    internal static readonly string[] AspireHostingDirectPackages =
    [
        "Aspire.Hosting",
        "Aspire.Hosting.Azure.Storage",
        "Aspire.Hosting.Orleans",
    ];

    internal static readonly string[] TestingDirectProjects =
    [
        Client,
        IntegrationsMcp,
        Kernel,
    ];

    internal static readonly string[] TestingDirectPackages =
    [
        "Aspire.Hosting.Testing",
        "Microsoft.Orleans.TestingHost",
        "xunit.v3.extensibility.core",
    ];

    internal static readonly string[] TestingCompileReachable =
    [
        Abstractions,
        Client,
        IntegrationsMcp,
        Kernel,
        Security,
    ];

    internal static bool IsModulesProject(string project) =>
        project.StartsWith(ModulesPrefix, StringComparison.Ordinal);

    internal static bool IsIntegrationsProject(string project) =>
        project.StartsWith(IntegrationsPrefix, StringComparison.Ordinal);

    internal static bool IsAspireFamilyProject(string project) =>
        project.StartsWith(AspireFamilyPrefix, StringComparison.Ordinal);

    internal static bool IsUiFamilyProject(string project) =>
        project is Ui || project.StartsWith(UiPrefix, StringComparison.Ordinal);

    internal static bool IsForbiddenOnConsumerResidual(string project) =>
        project is Kernel or Security or Testing or AspireHosting
        || IsIntegrationsProject(project)
        || IsModulesProject(project);

    internal static bool IsForbiddenOnAspireHostingProject(string project) =>
        project is Kernel or Client or Aspire or Security or Testing
        || IsModulesProject(project)
        || IsIntegrationsProject(project)
        || IsUiFamilyProject(project);

    internal static bool IsForbiddenOnIntegrationsMcpProject(string project) =>
        project is Kernel or Client or Testing
        || IsModulesProject(project)
        || IsAspireFamilyProject(project);

    internal static bool IsForbiddenOnIntegrationsMcpPackage(string package) =>
        package is "ModelContextProtocol"
            or "ModelContextProtocol.AspNetCore"
            or "Microsoft.AspNetCore.DataProtection"
        || package.StartsWith("OpenAI", StringComparison.Ordinal)
        || package.StartsWith("OllamaSharp", StringComparison.Ordinal)
        || package.StartsWith("Microsoft.Agents.AI", StringComparison.Ordinal);

    internal static bool IsForbiddenOnTestingProject(string project) =>
        IsModulesProject(project)
        || IsAspireFamilyProject(project)
        || IsUiFamilyProject(project);
}
