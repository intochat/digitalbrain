using System.Reflection;
using System.Xml.Linq;
using DigitalBrain.Abstractions;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TimeContracts
{
    private static readonly Assembly Contracts = typeof(ICountdown).Assembly;
    private static readonly string RepositoryRoot = LocateRepositoryRoot();
    private static readonly string ContractsDirectory = Path.Combine(
        RepositoryRoot,
        "modules",
        "DigitalBrain.Modules.Time.Contracts");

    [Fact]
    public void CountdownIsTheOnlyTimeNeuronCapability()
    {
        var exported = Contracts
            .GetExportedTypes()
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
        var vocabulary = exported
            .Where(type => type.Namespace == "DigitalBrain.Time")
            .ToArray();
        var neurons = vocabulary
            .Where(type =>
                type.IsInterface
                && typeof(INeuron).IsAssignableFrom(type))
            .ToArray();

        Assert.Equal([typeof(ICountdown)], neurons);
        Assert.Null(Contracts.GetType("DigitalBrain.Time.IReminder"));
        Assert.DoesNotContain(exported, type => type.Name == "IReminder");
        Assert.Equal(
            [
                nameof(CancelCountdown),
                nameof(CountdownElapsed),
                nameof(CountdownResolution),
                nameof(CountdownSnapshot),
                nameof(CountdownStatus),
                nameof(ICountdown),
                nameof(RescheduleCountdown),
                nameof(RestartCountdown),
                nameof(StartCountdown),
            ],
            vocabulary.Select(type => type.Name));
    }

    [Fact]
    public void CountdownMethodsKeepTheSettledUnsuffixedSignatures()
    {
        Assert.Contains(typeof(INeuron), typeof(ICountdown).GetInterfaces());
        Assert.NotNull(
            typeof(ICountdown).GetCustomAttribute<ClientEntryPointAttribute>(
                inherit: false));
        Assert.Equal(
            "DigitalBrain.Time.ICountdown",
            DeclaredAlias(typeof(ICountdown)));

        var expected = new Dictionary<string, Type[]>(StringComparer.Ordinal)
        {
            [nameof(ICountdown.Start)] = [typeof(StartCountdown)],
            [nameof(ICountdown.Reschedule)] = [typeof(RescheduleCountdown)],
            [nameof(ICountdown.Cancel)] = [typeof(CancelCountdown)],
            [nameof(ICountdown.Restart)] = [typeof(RestartCountdown)],
            [nameof(ICountdown.Read)] = [],
        };
        var methods = typeof(ICountdown)
            .GetMethods(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly);

        Assert.Equal(
            expected.Keys.Order(StringComparer.Ordinal),
            methods.Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.All(methods, method =>
        {
            Assert.DoesNotContain(
                "Async",
                method.Name,
                StringComparison.Ordinal);
            Assert.Equal(
                typeof(Task<CountdownSnapshot>),
                method.ReturnType);
            Assert.Equal(
                expected[method.Name],
                method.GetParameters()
                    .Select(parameter => parameter.ParameterType));
            Assert.Equal(
                method.Name,
                method.GetCustomAttribute<AliasAttribute>()?.Alias);
        });

        var source = File.ReadAllText(
            Path.Combine(ContractsDirectory, "ICountdown.cs"));
        Assert.Contains(
            "public partial interface ICountdown : INeuron",
            source,
            StringComparison.Ordinal);
        Assert.All(
            expected.Keys,
            method => Assert.Contains(
                $"[Alias(nameof({method}))]",
                source,
                StringComparison.Ordinal));
    }

    [Fact]
    public void CountdownCommandsKeepTheirExactWireShapes()
    {
        AssertSerializableRecord<StartCountdown>(
            "time.start-countdown",
            [
                (nameof(StartCountdown.CommandId), typeof(CommandId), 0u),
                (nameof(StartCountdown.Duration), typeof(TimeSpan), 1u),
                (nameof(StartCountdown.Destination), typeof(NeuronId), 2u),
            ]);
        AssertSerializableRecord<RescheduleCountdown>(
            "time.reschedule-countdown",
            [
                (nameof(RescheduleCountdown.CommandId), typeof(CommandId), 0u),
                (nameof(RescheduleCountdown.ExpectedRevision), typeof(long), 1u),
                (nameof(RescheduleCountdown.Duration), typeof(TimeSpan), 2u),
            ]);
        AssertSerializableRecord<CancelCountdown>(
            "time.cancel-countdown",
            [
                (nameof(CancelCountdown.CommandId), typeof(CommandId), 0u),
                (nameof(CancelCountdown.ExpectedRevision), typeof(long), 1u),
            ]);
        AssertSerializableRecord<RestartCountdown>(
            "time.restart-countdown",
            [
                (nameof(RestartCountdown.CommandId), typeof(CommandId), 0u),
                (nameof(RestartCountdown.Duration), typeof(TimeSpan), 1u),
            ]);

        Assert.Null(typeof(RestartCountdown).GetProperty("Destination"));
        Assert.Null(typeof(RestartCountdown).GetProperty("ExpectedRevision"));
        Assert.NotNull(typeof(StartCountdown).GetProperty("Destination"));
        Assert.Null(typeof(RescheduleCountdown).GetProperty("Destination"));
        Assert.Null(typeof(CancelCountdown).GetProperty("Destination"));
        Assert.NotNull(
            typeof(RescheduleCountdown).GetProperty("ExpectedRevision"));
        Assert.NotNull(
            typeof(CancelCountdown).GetProperty("ExpectedRevision"));
        Assert.Null(typeof(StartCountdown).GetProperty("ExpectedRevision"));

        var source = File.ReadAllText(
            Path.Combine(ContractsDirectory, "CountdownCommands.cs"));
        Assert.All(
            new[]
            {
                typeof(StartCountdown),
                typeof(RescheduleCountdown),
                typeof(CancelCountdown),
                typeof(RestartCountdown),
            },
            command => Assert.Contains(
                $"public sealed record {command.Name}(",
                source,
                StringComparison.Ordinal));
    }

    [Fact]
    public void SnapshotAndElapsedFactKeepTheirExactWireShapes()
    {
        AssertSerializableRecord<CountdownSnapshot>(
            "time.countdown-snapshot",
            [
                (nameof(CountdownSnapshot.Status), typeof(CountdownStatus), 0u),
                (nameof(CountdownSnapshot.Generation), typeof(long), 1u),
                (nameof(CountdownSnapshot.Revision), typeof(long), 2u),
                (nameof(CountdownSnapshot.Destination), typeof(NeuronId?), 3u),
                (nameof(CountdownSnapshot.ScheduledAt), typeof(DateTimeOffset?), 4u),
                (nameof(CountdownSnapshot.DueAt), typeof(DateTimeOffset?), 5u),
                (nameof(CountdownSnapshot.Duration), typeof(TimeSpan?), 6u),
            ]);
        AssertSerializableRecord<CountdownElapsed>(
            "time.countdown-elapsed",
            [
                (nameof(CountdownElapsed.Countdown), typeof(NeuronId), 0u),
                (nameof(CountdownElapsed.Generation), typeof(long), 1u),
                (nameof(CountdownElapsed.Revision), typeof(long), 2u),
                (nameof(CountdownElapsed.Destination), typeof(NeuronId), 3u),
                (nameof(CountdownElapsed.ScheduledAt), typeof(DateTimeOffset), 4u),
                (nameof(CountdownElapsed.DueAt), typeof(DateTimeOffset), 5u),
                (nameof(CountdownElapsed.ObservedAt), typeof(DateTimeOffset), 6u),
                (nameof(CountdownElapsed.Resolution), typeof(CountdownResolution), 7u),
            ]);

        Assert.True(typeof(Synapse).IsAssignableFrom(typeof(CountdownElapsed)));
        Assert.Equal(
            [
                nameof(CountdownElapsed.Countdown),
                nameof(CountdownElapsed.Generation),
                nameof(CountdownElapsed.Revision),
                nameof(CountdownElapsed.Destination),
                nameof(CountdownElapsed.ScheduledAt),
                nameof(CountdownElapsed.DueAt),
                nameof(CountdownElapsed.ObservedAt),
                nameof(CountdownElapsed.Resolution),
            ],
            PublicDeclaredProperties(typeof(CountdownElapsed)));
    }

    [Fact]
    public void PersistedCountdownEnumsKeepTheirAliasesAndNumericValues()
    {
        AssertEnum<CountdownStatus>(
            "time.countdown-status",
            [
                (nameof(CountdownStatus.Unscheduled), 0),
                (nameof(CountdownStatus.Scheduled), 1),
                (nameof(CountdownStatus.Elapsed), 2),
                (nameof(CountdownStatus.Cancelled), 3),
            ]);
        AssertEnum<CountdownResolution>(
            "time.countdown-resolution",
            [
                (nameof(CountdownResolution.OnTime), 0),
                (nameof(CountdownResolution.Recovered), 1),
            ]);
    }

    [Fact]
    public void TimeContractsAreOnePackableLeafBesideItsRuntime()
    {
        var projectPath = Path.Combine(
            ContractsDirectory,
            "DigitalBrain.Modules.Time.Contracts.csproj");
        var project = XDocument.Load(projectPath);
        var projectReferences = project
            .Descendants("ProjectReference")
            .ToArray();
        var flowingProjects = projectReferences
            .Where(reference =>
                !string.Equals(
                    (string?)reference.Attribute("ReferenceOutputAssembly"),
                    "false",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    (string?)reference.Attribute("PrivateAssets"),
                    "all",
                    StringComparison.OrdinalIgnoreCase))
            .Select(ReferenceName)
            .ToArray();

        Assert.Equal(["DigitalBrain.Abstractions"], flowingProjects);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(
            "true",
            project.Descendants("IsPackable").Single().Value,
            ignoreCase: true);
        Assert.Equal(
            "DigitalBrain.Time",
            project.Descendants("RootNamespace").Single().Value);

        var generator = Assert.Single(
            projectReferences,
            reference => ReferenceName(reference)
                == "DigitalBrain.SourceGeneration");
        Assert.Equal(
            "Analyzer",
            (string?)generator.Attribute("OutputItemType"));
        Assert.Equal(
            "false",
            (string?)generator.Attribute("ReferenceOutputAssembly"),
            ignoreCase: true);
        Assert.Equal(
            "all",
            (string?)generator.Attribute("PrivateAssets"),
            ignoreCase: true);

        var timeProjects = Directory
            .EnumerateFiles(
                RepositoryRoot,
                "*Time*.csproj",
                SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                Path.Combine(
                    "modules",
                    "DigitalBrain.Modules.Time.Contracts",
                    "DigitalBrain.Modules.Time.Contracts.csproj"),
                Path.Combine(
                    "modules",
                    "DigitalBrain.Modules.Time",
                    "DigitalBrain.Modules.Time.csproj"),
                Path.Combine(
                    "tests",
                    "DigitalBrain.Time.Tests",
                    "DigitalBrain.Time.Tests.csproj"),
            ],
            timeProjects);

        var solution = File.ReadAllText(
            Path.Combine(RepositoryRoot, "DigitalBrain.slnx"));
        Assert.Equal(
            1,
            CountOccurrences(
                solution,
                "modules/DigitalBrain.Modules.Time.Contracts/DigitalBrain.Modules.Time.Contracts.csproj"));
        Assert.Equal(
            1,
            CountOccurrences(
                solution,
                "modules/DigitalBrain.Modules.Time/DigitalBrain.Modules.Time.csproj"));
        Assert.Equal(
            1,
            CountOccurrences(
                solution,
                "tests/DigitalBrain.Time.Tests/DigitalBrain.Time.Tests.csproj"));
    }

    [Fact]
    public void TimeContractsContainNoOpenScopeSchedulingVocabulary()
    {
        var forbidden = new[]
        {
            "IReminder",
            "Calendar",
            "Cron",
            "Recurrence",
            "TimeZone",
            "Noda",
            "Ical",
            "Interval",
        };
        var offenders = Directory
            .EnumerateFiles(
                ContractsDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => forbidden
                .Where(token => File
                    .ReadAllText(path)
                    .Contains(token, StringComparison.Ordinal))
                .Select(token =>
                    $"{Path.GetRelativePath(RepositoryRoot, path)}:{token}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static void AssertSerializableRecord<T>(
        string alias,
        (string Name, Type Type, uint Id)[] expected)
    {
        var type = typeof(T);

        Assert.True(type.IsClass);
        Assert.True(type.IsSealed);
        Assert.Equal(
            alias,
            DeclaredAlias(type));
        Assert.NotNull(
            type.GetCustomAttribute<GenerateSerializerAttribute>(
                inherit: false));
        Assert.Equal(
            expected.Select(member => member.Name),
            PublicDeclaredProperties(type));
        Assert.Equal(expected, SerializedMembers(type));
    }

    private static void AssertEnum<T>(
        string alias,
        (string Name, int Value)[] expected)
        where T : struct, Enum
    {
        var type = typeof(T);

        Assert.Equal(
            alias,
            DeclaredAlias(type));
        Assert.NotNull(
            type.GetCustomAttribute<GenerateSerializerAttribute>(
                inherit: false));
        Assert.Equal(
            expected,
            Enum.GetNames<T>()
                .Select(name => (
                    name,
                    Convert.ToInt32(
                        Enum.Parse<T>(name),
                        System.Globalization.CultureInfo.InvariantCulture)))
                .ToArray());
    }

    private static string[] PublicDeclaredProperties(Type type)
        => type
            .GetProperties(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .ToArray();

    private static (string Name, Type Type, uint Id)[] SerializedMembers(
        Type type)
        => type
            .GetProperties(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly)
            .Select(property => (
                property.Name,
                property.PropertyType,
                Id: property.GetCustomAttribute<IdAttribute>()?.Id))
            .Where(member => member.Id.HasValue)
            .OrderBy(member => member.Id)
            .Select(member => (
                member.Name,
                member.PropertyType,
                member.Id!.Value))
            .ToArray();

    private static string ReferenceName(XElement reference)
        => Path.GetFileNameWithoutExtension(
            reference.Attribute("Include")!.Value.Replace('\\', '/'));

    private static string? DeclaredAlias(Type type)
        => type
            .GetCustomAttributes<AliasAttribute>(inherit: false)
            .SingleOrDefault()
            ?.Alias;

    private static bool IsBuildOutput(string path)
        => Path.GetRelativePath(RepositoryRoot, path)
            .Split(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            .Any(segment =>
                segment.Equals(
                    "bin",
                    StringComparison.OrdinalIgnoreCase)
                || segment.Equals(
                    "obj",
                    StringComparison.OrdinalIgnoreCase));

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;

        while ((offset = source.IndexOf(
                   value,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(
                   Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "DigitalBrain.slnx was not found above the test assembly.");
    }
}
