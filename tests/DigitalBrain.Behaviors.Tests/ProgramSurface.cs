namespace DigitalBrain.Behaviors.Tests;

using System.Reflection;
using DigitalBrain.Abstractions;
using Xunit;

public sealed class ProgramSurface
{
    [Fact(DisplayName = "Behavior context exposes only deterministic behavior authority")]
    public void ContextExposesNoInfrastructureAuthority()
    {
        var exposed = typeof(IBehaviorContext).GetMembers()
            .SelectMany(MemberSignature.AllTypes)
            .ToArray();

        Assert.DoesNotContain(exposed, type =>
            type == typeof(IServiceProvider)
            || type.FullName is "Orleans.IGrainFactory" or "System.Net.Http.HttpClient");
        Assert.Equal(
            ["DeterministicCommandId", "Get", "ReadStateAsync", "SetState"],
            typeof(IBehaviorContext).GetMethods()
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            [("Execution", typeof(BehaviorExecutionMetadata)), ("UtcNow", typeof(DateTimeOffset))],
            typeof(IBehaviorContext).GetProperties()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => (property.Name, property.PropertyType)));
    }

    [Fact(DisplayName = "Behavior programs accept only a synapse trigger and constrained context")]
    public void BehaviorProgramUsesTheSafeExecutionSignature()
    {
        var program = typeof(IBehaviorProgram<>);
        var execute = Assert.Single(program.GetMethods());

        Assert.True(program.GetGenericArguments()[0].GetGenericParameterConstraints().Contains(typeof(Synapse)));
        Assert.Equal(typeof(ValueTask), execute.ReturnType);
        Assert.Equal(
            [program.GetGenericArguments()[0], typeof(IBehaviorContext), typeof(CancellationToken)],
            execute.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact(DisplayName = "Intent programs use the safe request-context-response signature")]
    public void IntentProgramUsesTheSafeExecutionSignature()
    {
        var program = typeof(IIntentProgram<,>);
        var execute = Assert.Single(program.GetMethods());
        var arguments = program.GetGenericArguments();

        Assert.Equal(typeof(ValueTask<>).MakeGenericType(arguments[1]), execute.ReturnType);
        Assert.Equal(
            [arguments[0], typeof(IBehaviorContext), typeof(CancellationToken)],
            execute.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact(DisplayName = "Execution metadata carries the immutable owner, behavior, revision, and execution identities")]
    public void ExecutionMetadataCarriesTheExecutionIdentity()
    {
        var properties = typeof(BehaviorExecutionMetadata).GetProperties()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => (property.Name, property.PropertyType))
            .ToArray();

        Assert.Equal(
            [
                ("Behavior", typeof(BehaviorId)),
                ("Execution", typeof(BehaviorExecutionId)),
                ("Owner", typeof(OwnerId)),
                ("Revision", typeof(BehaviorRevisionId)),
            ],
            properties);
        Assert.NotNull(typeof(BehaviorExecutionMetadata).GetCustomAttribute<GenerateSerializerAttribute>());
        Assert.Equal("db.behavior-execution-metadata", typeof(BehaviorExecutionMetadata).GetCustomAttribute<AliasAttribute>()?.Alias);
        Assert.Equal(
            [0, 1, 2, 3],
            typeof(BehaviorExecutionMetadata).GetProperties()
                .OrderBy(property => property.GetCustomAttribute<IdAttribute>()?.Id)
                .Select(property => property.GetCustomAttribute<IdAttribute>()?.Id));
    }
}

internal static class MemberSignature
{
    public static IEnumerable<Type> AllTypes(MemberInfo member)
        => member switch
        {
            MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)],
            PropertyInfo property => [property.PropertyType],
            _ => [],
        };
}
