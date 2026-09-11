using System.Reflection;
using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.UI;
using Orleans;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class FlutterWireGoldenFacts
{
    // ButtonClicked left with user-action machinery; Excel owns the other two outside UI.Contracts.
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.Ordinal)
    {
        "ButtonClicked", "ExcelState", "ExcelRow",
    };

    [Fact]
    public void Flutter_wire_members_match_the_golden()
    {
        var assembly = typeof(UIJson).Assembly;
        using var golden = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(assembly.Location)!, "flutter-wire-contracts.golden.json")));
        var types = assembly.GetTypes().ToDictionary(type => type.Name, StringComparer.Ordinal);
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        var renamedMembers = new List<string>();
        var renamedTypes = new List<string>();
        foreach (var entry in golden.RootElement.GetProperty("types").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString()!;
            var currentName = name;
            if (name == "ControlActivated")
            {
                // The signal body is now called ControlActivation; its wire alias and members remain.
                Assert.False(types.ContainsKey(name));
                currentName = nameof(ControlActivation);
                renamedTypes.Add(name);
            }

            if (!types.TryGetValue(currentName, out var type))
            {
                unresolved.Add(name);
                continue;
            }

            Assert.Equal(entry.GetProperty("alias").GetString(), type.GetCustomAttribute<AliasAttribute>(inherit: false)?.Alias);
            foreach (var member in entry.GetProperty("properties").EnumerateArray())
            {
                var propertyName = member.GetProperty("name").GetString()!;
                if (name == nameof(OpenSurface) && propertyName == "CommandId")
                {
                    // OpenSurface became a Command DTO and inherits Id in place of CommandId.
                    Assert.True(typeof(Command).IsAssignableFrom(type));
                    Assert.Null(type.GetProperty(propertyName));
                    Assert.Equal(typeof(CommandId), type.GetProperty(nameof(Command.Id))?.PropertyType);
                    renamedMembers.Add($"{name}.{propertyName}");
                    propertyName = nameof(Command.Id);
                }

                var property = type.GetProperty(propertyName);
                Assert.NotNull(property);
                Assert.Equal(member.GetProperty("type").GetString(), SimpleTypeName(property.PropertyType));
            }
        }

        Assert.Equal(ExcludedNames.Order(StringComparer.Ordinal), unresolved.Order(StringComparer.Ordinal));
        Assert.Equal(["ControlActivated"], renamedTypes);
        Assert.Equal(["OpenSurface.CommandId"], renamedMembers);
    }

    private static string SimpleTypeName(Type type)
        => type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(SimpleTypeName))}>"
            : type.Name;
}
