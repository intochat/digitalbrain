using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorHostOperationBoundary
{
    private static readonly Type[] BoundaryTypes =
    [
        typeof(BehaviorExecutionRequest),
        typeof(BehaviorHostExecuteCommand),
    ];

    private static readonly string[] ForbiddenTypeNames =
    [
        nameof(IBehaviorCapabilityResolver),
        "IGrainFactory",
        nameof(TimeProvider),
    ];

    [Fact(DisplayName =
        "BehaviorExecutionRequest carries Task, Attempt, TriggerPayload, exact grants, and UtcNow without raw trigger or silo objects")]
    public void BehaviorExecutionRequestSurfaceIsHardened()
    {
        AssertHardenedBoundary(typeof(BehaviorExecutionRequest));
    }

    [Fact(DisplayName =
        "BehaviorHostExecuteCommand carries Task, Attempt, TriggerPayload, exact grants, and UtcNow without raw trigger or silo objects")]
    public void BehaviorHostExecuteCommandSurfaceIsHardened()
    {
        AssertHardenedBoundary(typeof(BehaviorHostExecuteCommand));
    }

    [Fact(DisplayName =
        "OS BehaviorHost Program does not register HostGrainCapabilityResolver, IBehaviorCapabilityResolver, or IGrainFactory")]
    public void BehaviorHostProgramHasNoDirectGrainCapabilityResolver()
    {
        var programPath = Path.Combine(
            FindRepositoryRoot(),
            "os",
            "DigitalBrain.OS.BehaviorHost",
            "Program.cs");
        Assert.True(File.Exists(programPath), $"Missing BehaviorHost Program at {programPath}");

        var source = File.ReadAllText(programPath);
        Assert.DoesNotContain("HostGrainCapabilityResolver", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IBehaviorCapabilityResolver", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IGrainFactory", source, StringComparison.Ordinal);
    }

    private static void AssertHardenedBoundary(Type type)
    {
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToArray();

        RequireProperty(type, properties, "Task", typeof(NeuronId));
        RequireProperty(type, properties, "Attempt", typeof(AttemptId));
        RequireProperty(type, properties, "TriggerPayload", typeof(ProtectedPayloadReference));
        RequireProperty(
            type,
            properties,
            "Capabilities",
            typeof(IReadOnlyList<BehaviorCapabilityEdge>));
        RequireProperty(type, properties, "UtcNow", typeof(DateTimeOffset));

        Assert.DoesNotContain(
            properties,
            property => string.Equals(property.Name, "TriggerJson", StringComparison.Ordinal));

        foreach (var property in properties)
        {
            Assert.False(
                IsForbiddenBoundaryType(property.PropertyType),
                $"{type.Name}.{property.Name} must not be typed as {property.PropertyType.FullName}");
        }

        Assert.Contains(type, BoundaryTypes);
    }

    private static void RequireProperty(
        Type declaringType,
        PropertyInfo[] properties,
        string name,
        Type expectedType)
    {
        var property = properties.SingleOrDefault(
            candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        Assert.True(
            property is not null,
            $"{declaringType.Name} must expose public property '{name}' of type {Format(expectedType)}");
        Assert.True(
            TypesMatch(property!.PropertyType, expectedType),
            $"{declaringType.Name}.{name} is {Format(property.PropertyType)}, expected {Format(expectedType)}");
    }

    private static bool TypesMatch(Type actual, Type expected)
    {
        if (actual == expected)
        {
            return true;
        }

        if (!actual.IsGenericType || !expected.IsGenericType)
        {
            return false;
        }

        if (actual.GetGenericTypeDefinition() != expected.GetGenericTypeDefinition())
        {
            return false;
        }

        var actualArgs = actual.GetGenericArguments();
        var expectedArgs = expected.GetGenericArguments();
        if (actualArgs.Length != expectedArgs.Length)
        {
            return false;
        }

        for (var index = 0; index < actualArgs.Length; index++)
        {
            if (!TypesMatch(actualArgs[index], expectedArgs[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsForbiddenBoundaryType(Type type)
    {
        if (ForbiddenTypeNames.Contains(type.Name, StringComparer.Ordinal))
        {
            return true;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (IsForbiddenBoundaryType(argument))
                {
                    return true;
                }
            }
        }

        return type.GetInterfaces().Any(face =>
            ForbiddenTypeNames.Contains(face.Name, StringComparer.Ordinal));
    }

    private static string Format(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var args = string.Join(", ", type.GetGenericArguments().Select(Format));
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        return $"{name}<{args}>";
    }

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
