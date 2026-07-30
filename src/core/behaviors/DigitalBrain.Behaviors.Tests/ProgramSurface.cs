namespace DigitalBrain.Behaviors.Tests;

using System.Collections.Immutable;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

public sealed class ProgramSurface
{
    [Fact(DisplayName = "Single-file behavior SDK compiles ConnectAsync Trigger Get SendAsync surface")]
    public void SingleFileSdkSurfaceCompiles()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var compilation = CSharpCompilation.Create(
            "BehaviorSdkSurface",
            [CSharpSyntaxTree.ParseText(RailPrograms.SingleFileSdkProgram(), cancellationToken: cancellation)],
            SdkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: cancellation);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

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
            [
                ("AttemptCancellation", typeof(CancellationToken)),
                ("Execution", typeof(BehaviorExecutionMetadata)),
                ("UtcNow", typeof(DateTimeOffset)),
            ],
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
        var name = Assert.Single(get.GetParameters());
        Assert.Equal(typeof(string), name.ParameterType);
        Assert.True(name.HasDefaultValue);
        Assert.Equal("default", name.DefaultValue);
        Assert.Equal(contract, get.ReturnType);
    }

    [Fact(DisplayName = "Omitted SendAsync token links to the worker attempt cancellation")]
    public async Task OmittedSendTokenLinksToAttemptCancellation()
    {
        using var attempt = new CancellationTokenSource();
        await attempt.CancelAsync();
        await using var brain = new BehaviorBrain<SdkResearchCompanyRequest>(
            new BehaviorTrigger<SdkResearchCompanyRequest>(new SdkResearchCompanyRequest("q"), attempt.Token));

        var gmail = brain.Get<ISdkGmail>();
        var request = new SdkGmailRequest("q");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(SendOmittingToken);

        Task SendOmittingToken() => gmail.SendAsync(request);
    }

    [Fact(DisplayName = "Caller token is linked with the worker attempt cancellation")]
    public async Task CallerTokenIsLinkedWithAttemptCancellation()
    {
        using var attempt = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        await using var brain = new BehaviorBrain<SdkResearchCompanyRequest>(
            new BehaviorTrigger<SdkResearchCompanyRequest>(new SdkResearchCompanyRequest("q"), attempt.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => brain.Get<ISdkGmail>().SendAsync(new SdkGmailRequest("q"), caller.Token));
    }

    [Fact(DisplayName = "Behavior trigger rejects a non-cancellable attempt token")]
    public void BehaviorTriggerRejectsNonCancellableAttemptToken()
    {
        Assert.Throws<ArgumentException>(() =>
            new BehaviorTrigger<SdkResearchCompanyRequest>(
                new SdkResearchCompanyRequest("q"),
                default));
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

    private static ImmutableArray<MetadataReference> SdkReferences()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        void Add(Assembly assembly)
        {
            if (string.IsNullOrWhiteSpace(assembly.Location) || !set.Add(assembly.Location))
            {
                return;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        Add(typeof(object).Assembly);
        Add(typeof(Enumerable).Assembly);
        Add(typeof(INeuron).Assembly);
        Add(typeof(IBehaviorProgram<>).Assembly);
        Add(typeof(DigitalBrainClient).Assembly);
        Add(typeof(BehaviorBrain<>).Assembly);
        Add(typeof(Orleans.IGrain).Assembly);
        Add(Assembly.Load("System.Runtime"));
        Add(Assembly.Load("System.Collections"));
        Add(Assembly.Load("System.Linq"));
        Add(Assembly.Load("System.Private.CoreLib"));
        Add(Assembly.Load("netstandard"));
        Add(Assembly.Load("System.Threading"));
        Add(Assembly.Load("System.Threading.Tasks"));
        return [.. references];
    }
}

internal sealed record SdkResearchCompanyRequest(string Prompt) : Synapse;
internal sealed record SdkGmailResponse(string Status) : Synapse;
internal sealed record SdkGmailRequest(string Prompt) : RequestSynapse<SdkGmailResponse>;
internal interface ISdkGmail : INeuron;

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
