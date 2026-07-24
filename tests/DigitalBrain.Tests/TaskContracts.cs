using System.Reflection;
using System.Xml.Linq;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TaskContracts
{
    private static readonly Assembly Contracts = typeof(ITask).Assembly;
    private static readonly Assembly Runtime = typeof(TasksModule).Assembly;
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact(DisplayName = "Tasks contracts are a leaf over Abstractions and contain no integration vocabulary")]
    public void TasksContractsRemainIndependent()
    {
        var project = XDocument.Load(Path.Combine(
            RepositoryRoot,
            "modules",
            "DigitalBrain.Modules.Tasks.Contracts",
            "DigitalBrain.Modules.Tasks.Contracts.csproj"));
        var projectReferences = project
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(
                reference.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToArray();
        var forbidden = new[]
        {
            "DigitalBrain.AI",
            "Microsoft.Agents",
            "Microsoft.Extensions.AI",
            "ModelContextProtocol",
            "DigitalBrain.Google",
            "DigitalBrain.Salesforce",
            "DigitalBrain.Time",
        };
        var surface = Contracts.GetExportedTypes()
            .SelectMany(type => type.GetMembers().Select(member => $"{type.FullName} {member}"))
            .ToArray();

        Assert.Equal(
            ["DigitalBrain.Abstractions", "DigitalBrain.SourceGeneration"],
            projectReferences);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.DoesNotContain(
            Contracts.GetReferencedAssemblies(),
            reference => forbidden.Any(name => reference.Name?.Contains(name, StringComparison.Ordinal) is true));
        Assert.DoesNotContain(
            surface,
            member => forbidden.Any(name => member.Contains(name, StringComparison.Ordinal)));
    }

    [Fact(DisplayName = "TaskNeuron stays internal behind the stable ITask vocabulary")]
    public void TaskNeuronStaysInternal()
    {
        var neuron = Runtime.GetType("DigitalBrain.Tasks.TaskNeuron", throwOnError: true)!;

        Assert.False(neuron.IsPublic);
        Assert.Contains(typeof(ITask), neuron.GetInterfaces());
        Assert.DoesNotContain(Runtime.GetExportedTypes(), type => type.Name == "TaskNeuron");
    }

    [Fact(DisplayName = "all Attempt facts share the fenced Task Worker Attempt and Revision envelope")]
    public void AttemptFactsShareOneEnvelope()
    {
        var facts = Contracts.GetExportedTypes()
            .Where(type => type.IsSubclassOf(typeof(AttemptFact)))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(8, facts.Length);
        Assert.All(facts, fact =>
        {
            Assert.True(typeof(Synapse).IsAssignableFrom(fact));
            Assert.NotNull(fact.GetProperty(nameof(AttemptFact.Task)));
            Assert.NotNull(fact.GetProperty(nameof(AttemptFact.Worker)));
            Assert.NotNull(fact.GetProperty(nameof(AttemptFact.Attempt)));
            Assert.NotNull(fact.GetProperty(nameof(AttemptFact.Revision)));
        });
    }

    [Fact(DisplayName = "domain command, Attempt, and blocker identities reject empty values")]
    public void EmptyDomainIdsAreRejected()
    {
        _ = Assert.Throws<ArgumentException>(() => new CommandId(Guid.Empty));
        _ = Assert.Throws<ArgumentException>(() => new AttemptId(Guid.Empty));
        _ = Assert.Throws<ArgumentException>(() => new BlockerId(Guid.Empty));
        _ = Assert.Throws<ArgumentException>(() => new InputRequired(default));
    }

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
}
