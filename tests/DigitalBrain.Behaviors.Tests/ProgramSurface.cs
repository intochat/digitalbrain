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

        var trigger = Assert.Single(program.GetGenericArguments());

        Assert.Equal(GenericParameterAttributes.Contravariant, trigger.GenericParameterAttributes & GenericParameterAttributes.VarianceMask);
        Assert.Equal([typeof(Synapse)], trigger.GetGenericParameterConstraints());
        Assert.Equal(nameof(IBehaviorProgram<Synapse>.ExecuteAsync), execute.Name);
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

        Assert.All(arguments, argument => Assert.Equal(GenericParameterAttributes.None, argument.GenericParameterAttributes & GenericParameterAttributes.VarianceMask));
        Assert.Equal(nameof(IIntentProgram<object, object>.ExecuteAsync), execute.Name);
        Assert.Equal(typeof(ValueTask<>).MakeGenericType(arguments[1]), execute.ReturnType);
        Assert.Equal(
            [arguments[0], typeof(IBehaviorContext), typeof(CancellationToken)],
            execute.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact(DisplayName = "Behavior context Get keeps the exact class and INeuron constraints")]
    public void ContextGetKeepsTheExactNeuronContractConstraint()
    {
        var get = typeof(IBehaviorContext).GetMethod(nameof(IBehaviorContext.Get));
        Assert.NotNull(get);
        var contract = Assert.Single(get!.GetGenericArguments());

        Assert.Equal(GenericParameterAttributes.ReferenceTypeConstraint, contract.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask);
        Assert.Equal([typeof(INeuron)], contract.GetGenericParameterConstraints());
        Assert.Equal(typeof(string), Assert.Single(get.GetParameters()).ParameterType);
        Assert.Equal(contract, get.ReturnType);
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
        Assert.Equal(0u, typeof(BehaviorExecutionMetadata).GetProperty(nameof(BehaviorExecutionMetadata.Owner))?.GetCustomAttribute<IdAttribute>()?.Id);
        Assert.Equal(1u, typeof(BehaviorExecutionMetadata).GetProperty(nameof(BehaviorExecutionMetadata.Behavior))?.GetCustomAttribute<IdAttribute>()?.Id);
        Assert.Equal(2u, typeof(BehaviorExecutionMetadata).GetProperty(nameof(BehaviorExecutionMetadata.Revision))?.GetCustomAttribute<IdAttribute>()?.Id);
        Assert.Equal(3u, typeof(BehaviorExecutionMetadata).GetProperty(nameof(BehaviorExecutionMetadata.Execution))?.GetCustomAttribute<IdAttribute>()?.Id);
    }

    [Fact(DisplayName = "Execution metadata rejects uninitialized behavior identities")]
    public void ExecutionMetadataRejectsUninitializedBehaviorIdentities()
    {
        Assert.Throws<InvalidOperationException>(() => new BehaviorExecutionMetadata(
            new OwnerId("owner"),
            default,
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            new BehaviorExecutionId(new Guid("4b050fe8-45d0-4a16-b6a5-1b4b6683880a"))));
    }

    [Fact(DisplayName = "Execution metadata preserves its PascalCase named-argument constructor contract")]
    public void ExecutionMetadataAcceptsOriginalPascalCaseNamedArguments()
    {
        var metadata = new BehaviorExecutionMetadata(
            Owner: new OwnerId("owner"),
            Behavior: new BehaviorId("com.digitalbrain.start-ui"),
            Revision: new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            Execution: new BehaviorExecutionId(new Guid("4b050fe8-45d0-4a16-b6a5-1b4b6683880a")));

        Assert.Equal("owner", metadata.Owner.Value);
        Assert.Equal("com.digitalbrain.start-ui", metadata.Behavior.Value);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", metadata.Revision.Value);
        Assert.Equal(new Guid("4b050fe8-45d0-4a16-b6a5-1b4b6683880a"), metadata.Execution.Value);
    }
}

internal static class MemberSignature
{
    public static IEnumerable<Type> AllTypes(MemberInfo member)
        => DirectTypes(member)
            .Concat(member is MethodInfo method ? method.GetGenericArguments() : [])
            .SelectMany(Expand);

    private static IEnumerable<Type> DirectTypes(MemberInfo member)
        => member switch
        {
            MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)],
            PropertyInfo property => [property.PropertyType],
            _ => [],
        };

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;

        if (type.IsGenericParameter)
        {
            foreach (var constraint in type.GetGenericParameterConstraints())
            {
                foreach (var nested in Expand(constraint))
                {
                    yield return nested;
                }
            }
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var nested in Expand(argument))
                {
                    yield return nested;
                }
            }
        }
    }
}
