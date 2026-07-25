using System.Reflection;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using DigitalBrain.Flutter;
using DigitalBrain.Tests.Boundary;
using DigitalBrain.Tests.Packages;
using Xunit;

namespace DigitalBrain.Tests.Flutter;

public sealed class FlutterContracts
{
    [Fact]
    public void PublicVocabularyIsFirstVerticalSurfaceOnly()
    {
        var contracts = typeof(IShell).Assembly;
        var flutterNamespace = typeof(IShell).Namespace;

        var vocabulary = contracts
            .GetExportedTypes()
            .Where(type => type.Namespace == flutterNamespace)
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
        Assert.Null(contracts.GetType($"DigitalBrain.UI.{nameof(IShell)}"));
    }

    [Fact]
    public void WireContractGoldenMatchesContractsAssembly()
    {
        var actual = ExtractWireManifest(typeof(IShell).Assembly);
        var goldenPath = Path.Combine(
            PackageBoundarySupport.RepositoryRoot,
            RepositoryLayout.Modules,
            PackageInventory.ModulesFlutterContracts,
            "flutter-wire-contracts.golden.json");
        Assert.True(File.Exists(goldenPath));

        var expected = JsonNode.Parse(File.ReadAllText(goldenPath))!;
        Assert.True(JsonNode.DeepEquals(expected, actual));
    }

    [Fact]
    public void ContractsReachNeitherFlutterNorDartSdks()
    {
        var references = typeof(IShell).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name!)
            .ToArray();

        Assert.DoesNotContain(references, PackageBoundarySupport.IsDartOrFlutterSdkPackage);
    }

    private static JsonObject ExtractWireManifest(Assembly assembly)
    {
        var flutterNamespace = typeof(IShell).Namespace;
        var types = assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == flutterNamespace)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .Select(DescribeType)
            .ToArray();

        return new JsonObject
        {
            ["version"] = 1,
            ["namespace"] = flutterNamespace,
            ["types"] = new JsonArray(types.Select(node => (JsonNode)node).ToArray()),
        };
    }

    private static JsonObject DescribeType(Type type)
    {
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
            ["alias"] = FirstAlias(type),
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
        => !method.IsSpecialName
            && method.Name is not (
                nameof(object.Equals)
                or nameof(object.GetHashCode)
                or nameof(object.ToString)
                or "Deconstruct"
                or "<Clone>$");

    private static string TypeDisplayName(Type type)
    {
        if (type == typeof(string))
        {
            return nameof(String);
        }

        if (type == typeof(Task))
        {
            return nameof(Task);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return $"{nameof(Task)}<{TypeDisplayName(type.GetGenericArguments()[0])}>";
        }

        return type.Name;
    }
}
