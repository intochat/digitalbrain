using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using DigitalBrain.Flutter;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class FlutterContracts
{
    [Fact(DisplayName = "Flutter public vocabulary is the first-vertical surface only")]
    public void FlutterPublicVocabularyIsTheFirstVerticalSurfaceOnly()
    {
        var contracts = typeof(IShell).Assembly;
        var vocabulary = contracts
            .GetExportedTypes()
            .Where(type => type.Namespace == "DigitalBrain.Flutter")
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(ControlActivated),
                nameof(IScene),
                nameof(IShell),
                nameof(OpenScene),
                nameof(SceneOpened),
            ],
            vocabulary);
        Assert.Null(contracts.GetType("DigitalBrain.Flutter.IFlutter"));
        Assert.Null(contracts.GetType("DigitalBrain.UI.IShell"));
    }

    [Fact(DisplayName = "Flutter namespace identity is DigitalBrain.Flutter not Modules or UI")]
    public void FlutterNamespaceIdentityIsDigitalBrainFlutter()
    {
        Assert.Equal("DigitalBrain.Flutter", typeof(IShell).Namespace);
        Assert.Equal("DigitalBrain.Flutter", typeof(IScene).Namespace);
        Assert.Equal("DigitalBrain.Flutter", typeof(SceneOpened).Namespace);
        Assert.Equal("DigitalBrain.Modules.Flutter.Contracts", typeof(IShell).Assembly.GetName().Name);
    }

    [Fact(DisplayName = "IShell Open is unsuffixed and alias-pinned")]
    public void IShellOpenIsUnsuffixedAndAliasPinned()
    {
        var open = typeof(IShell).GetMethod(
            nameof(IShell.Open),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotNull(open);
        Assert.DoesNotContain("Async", open.Name, StringComparison.Ordinal);
        Assert.Equal(nameof(IShell.Open), FirstAlias(open));
        Assert.Equal(typeof(Task), open.ReturnType);
        Assert.Equal(typeof(OpenScene), open.GetParameters().Single().ParameterType);
    }

    [Fact(DisplayName = "Flutter wire-contract golden matches Contracts assembly")]
    public void FlutterWireContractGoldenMatchesContractsAssembly()
    {
        var actual = ExtractWireManifest(typeof(IShell).Assembly);
        var goldenPath = Path.Combine(
            LocateRepositoryRoot(),
            "modules",
            "DigitalBrain.Modules.Flutter.Contracts",
            "flutter-wire-contracts.golden.json");
        Assert.True(File.Exists(goldenPath), $"Missing golden at {goldenPath}");

        var expected = JsonNode.Parse(File.ReadAllText(goldenPath))!;
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Wire contract drift.\nExpected:\n{expected.ToJsonString(Indented)}\nActual:\n{actual.ToJsonString(Indented)}");
    }

    [Fact(DisplayName = "Flutter contracts reach neither Kernel nor Flutter/Dart SDKs")]
    public void FlutterContractsReachNeitherKernelNorFlutterDartSdks()
    {
        var references = typeof(IShell).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name!)
            .ToArray();

        Assert.DoesNotContain(references, name => name == "DigitalBrain.Kernel");
        Assert.DoesNotContain(
            references,
            name => name.Contains("Flutter", StringComparison.OrdinalIgnoreCase)
                && name != "DigitalBrain.Modules.Flutter.Contracts");
        Assert.DoesNotContain(
            references,
            name => name.StartsWith("Dart", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static JsonObject ExtractWireManifest(Assembly assembly)
    {
        var types = assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == "DigitalBrain.Flutter")
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .Select(DescribeType)
            .ToArray();

        return new JsonObject
        {
            ["version"] = 1,
            ["namespace"] = "DigitalBrain.Flutter",
            ["types"] = new JsonArray(types.Select(node => (JsonNode)node).ToArray()),
        };
    }

    private static JsonObject DescribeType(Type type)
    {
        var alias = FirstAlias(type);
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is { IsPublic: true, IsStatic: false })
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new JsonObject
            {
                ["name"] = property.Name,
                ["type"] = TypeDisplayName(property.PropertyType),
            })
            .ToArray();

        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(IsWireMethod)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .Select(method => new JsonObject
            {
                ["name"] = method.Name,
                ["alias"] = FirstAlias(method),
                ["parameters"] = new JsonArray(
                    method.GetParameters()
                        .Select(parameter => (JsonNode)new JsonObject
                        {
                            ["name"] = parameter.Name,
                            ["type"] = TypeDisplayName(parameter.ParameterType),
                        })
                        .ToArray()),
                ["returnType"] = TypeDisplayName(method.ReturnType),
            })
            .ToArray();

        return new JsonObject
        {
            ["name"] = type.Name,
            ["kind"] = type.IsInterface ? "interface" : "record",
            ["alias"] = alias,
            ["properties"] = new JsonArray(properties.Select(node => (JsonNode)node).ToArray()),
            ["methods"] = new JsonArray(methods.Select(node => (JsonNode)node).ToArray()),
        };
    }

    private static string? FirstAlias(MemberInfo member)
        => member
            .GetCustomAttributes<AliasAttribute>(inherit: false)
            .Select(attribute => attribute.Alias)
            .Distinct(StringComparer.Ordinal)
            .SingleOrDefault();

    private static bool IsWireMethod(MethodInfo method)
    {
        if (method.IsSpecialName)
        {
            return false;
        }

        return method.Name is not (
            "Equals"
            or "GetHashCode"
            or "ToString"
            or "Deconstruct"
            or "<Clone>$");
    }

    private static string TypeDisplayName(Type type)
    {
        if (type == typeof(string))
        {
            return "String";
        }

        if (type == typeof(Task))
        {
            return "Task";
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return $"Task<{TypeDisplayName(type.GetGenericArguments()[0])}>";
        }

        return type.Name;
    }

    private static string LocateRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate DigitalBrain.slnx from the test output directory.");
    }
}
