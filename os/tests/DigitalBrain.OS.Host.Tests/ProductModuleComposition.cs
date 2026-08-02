using System.Text.RegularExpressions;
using DigitalBrain.AI;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Chat;
using DigitalBrain.Google;
using DigitalBrain.Memory;
using DigitalBrain.OS.Assistant;
using DigitalBrain.Salesforce;
using DigitalBrain.Shell;
using DigitalBrain.Tasks;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed partial class ProductModuleComposition
{
    private static readonly Type[] ProductModuleTypes =
    [
        typeof(AIModule),
        typeof(ChatModule),
        typeof(MemoryModule),
        typeof(AssistantModule),
        typeof(ShellModule),
        typeof(GoogleModule),
        typeof(SalesforceModule),
        typeof(BehaviorsModule),
        typeof(TasksModule),
        typeof(TimeModule),
    ];

    [Fact(DisplayName = "Product AppHost selects TimeModule alongside the rest of the product module list")]
    public void ProductAppHostComposesTimeModule()
    {
        var root = FindRepositoryRoot();
        var appHost = File.ReadAllText(Path.Combine(root, "os", "DigitalBrain.OS.AppHost", "AppHost.cs"));

        Assert.Contains("using DigitalBrain.Time;", appHost, StringComparison.Ordinal);
        Assert.Contains("brain.AddModule<TimeModule>();", appHost, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Product AppHost and silo project reference DigitalBrain.Modules.Time, mirroring TasksModule")]
    public void ProductAppHostAndSiloReferenceTimeModule()
    {
        var root = FindRepositoryRoot();
        var appHostProject = File.ReadAllText(
            Path.Combine(root, "os", "DigitalBrain.OS.AppHost", "DigitalBrain.OS.AppHost.csproj"));
        var siloProject = File.ReadAllText(
            Path.Combine(root, "os", "DigitalBrain.OS.Host", "DigitalBrain.OS.Host.csproj"));

        const string timeProjectReference =
            @"src\modules\time\DigitalBrain.Modules.Time\DigitalBrain.Modules.Time.csproj";

        Assert.Contains(timeProjectReference, appHostProject, StringComparison.Ordinal);
        Assert.Contains(timeProjectReference, siloProject, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "docker-compose.yml module ids for digitalbrain and digitalbrain-ui match a real product module type's FullName")]
    public void DockerComposeModuleIdsMatchProductModuleTypes()
    {
        var root = FindRepositoryRoot();
        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));

        var moduleIdsByService = ParseModuleIdsByService(compose);

        Assert.True(
            moduleIdsByService.ContainsKey("digitalbrain"),
            "docker-compose.yml no longer defines a 'digitalbrain' service.");
        Assert.True(
            moduleIdsByService.ContainsKey("digitalbrain-ui"),
            "docker-compose.yml no longer defines a 'digitalbrain-ui' service.");

        var knownModuleFullNames = ProductModuleTypes
            .Select(type => type.FullName ?? throw new InvalidOperationException($"{type} has no FullName."))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (service, moduleIds) in moduleIdsByService)
        {
            Assert.NotEmpty(moduleIds);

            foreach (var moduleId in moduleIds)
            {
                Assert.True(
                    knownModuleFullNames.Contains(moduleId),
                    $"docker-compose.yml service '{service}' declares module id '{moduleId}', which does not " +
                    $"match any product module type's FullName ({string.Join(", ", knownModuleFullNames)}).");
            }
        }
    }

    private static Dictionary<string, List<string>> ParseModuleIdsByService(string composeYaml)
    {
        var moduleIdsByService = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? currentService = null;

        foreach (var rawLine in composeYaml.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var serviceHeaderMatch = ServiceHeaderPattern().Match(line);
            if (serviceHeaderMatch.Success)
            {
                currentService = serviceHeaderMatch.Groups[1].Value;
                continue;
            }

            var moduleIdMatch = ModuleIdPattern().Match(line);
            if (moduleIdMatch.Success && currentService is not null)
            {
                if (!moduleIdsByService.TryGetValue(currentService, out var moduleIds))
                {
                    moduleIds = [];
                    moduleIdsByService[currentService] = moduleIds;
                }

                moduleIds.Add(moduleIdMatch.Groups[1].Value.Trim());
            }
        }

        return moduleIdsByService;
    }

    [GeneratedRegex(@"^  ([A-Za-z0-9_-]+):\s*$")]
    private static partial Regex ServiceHeaderPattern();

    [GeneratedRegex(@"^\s{4,}DigitalBrain__Modules__\d+:\s*(\S+)\s*$")]
    private static partial Regex ModuleIdPattern();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find DigitalBrain.slnx above {AppContext.BaseDirectory}.");
    }
}
