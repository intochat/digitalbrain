using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class IntegrationContracts
{
    [Fact(DisplayName = "MCP providers expose no imitation client seams")]
    public void McpProvidersExposeNoImitationClientSeams()
    {
        var forbidden = new[]
        {
            "IMcpClient",
            "IMcpClientFactory",
            "SdkMcpClient",
            "SdkMcpClientFactory",
            "McpSession",
            "McpToolContract",
            "IMcpAuthorizationRedirect",
            "RejectingMcpAuthorizationRedirect",
        };
        var assemblies = new[]
        {
            Assembly.Load("DigitalBrain.Integrations.Mcp"),
            typeof(IGmail).Assembly,
            typeof(GoogleModule).Assembly,
            typeof(ISalesforce).Assembly,
            typeof(SalesforceModule).Assembly,
        };

        Assert.DoesNotContain(
            assemblies.SelectMany(assembly => assembly.GetTypes()),
            type => forbidden.Contains(type.Name, StringComparer.Ordinal));
    }

    [Fact(DisplayName = "Gmail exposes semantic message reading without MCP-shaped public types")]
    public void GmailContractIsSemanticAndProviderAgnostic()
    {
        Assert.Contains(typeof(INeuron), typeof(IGmail).GetInterfaces());

        var read = Assert.Single(typeof(IGmail).GetMethods());
        Assert.Equal(nameof(IGmail.ReadMessage), read.Name);
        Assert.Equal(typeof(Task<GmailMessage>), read.ReturnType);
        Assert.Equal(
            [typeof(string), typeof(CancellationToken)],
            read.GetParameters().Select(parameter => parameter.ParameterType));

        AssertProviderAgnostic(typeof(IGmail).Assembly);
        AssertProviderAgnostic(typeof(GoogleModule).Assembly);
    }

    [Fact(DisplayName = "Salesforce exposes an exact two-phase Account mutation contract")]
    public void SalesforceContractRequiresBoundApprovalAndModelsUncertainty()
    {
        Assert.Contains(typeof(INeuron), typeof(ISalesforce).GetInterfaces());

        var methods = typeof(ISalesforce).GetMethods()
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [nameof(ISalesforce.ApproveAccountDescription), nameof(ISalesforce.ProposeAccountDescription)],
            methods.Select(method => method.Name));
        Assert.All(methods, method =>
            Assert.Equal(typeof(Task<SalesforceAccountDescriptionMutation>), method.ReturnType));
        Assert.Equal(
            [
                "DigitalBrain.Salesforce.SalesforceMutationApproval",
                typeof(SynapseDelivery).FullName,
                typeof(CancellationToken).FullName,
            ],
            methods[0].GetParameters().Select(parameter => parameter.ParameterType.FullName));
        Assert.Equal(
            [typeof(CommandId), typeof(NeuronId), typeof(string), typeof(string), typeof(CancellationToken)],
            methods[1].GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [
                SalesforceMutationState.AwaitingApproval,
                SalesforceMutationState.Completed,
                SalesforceMutationState.OutcomeUncertain,
            ],
            Enum.GetValues<SalesforceMutationState>());
        Assert.True(typeof(Synapse).IsAssignableFrom(typeof(SalesforceMutationApproval)));
        Assert.Equal(
            ["ApprovalId", "ApprovedAt", "Approver", "CommandId", "Fingerprint"],
            typeof(SalesforceMutationApproval)
                .GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));

        AssertProviderAgnostic(typeof(ISalesforce).Assembly);
        AssertProviderAgnostic(typeof(SalesforceModule).Assembly);
    }

    [Fact(DisplayName = "Salesforce mutation ledger preserves its deployed serializer field IDs")]
    public void SalesforceMutationLedgerPreservesSerializerFieldIds()
    {
        var mutation = typeof(SalesforceModule).Assembly.GetType(
            "DigitalBrain.Salesforce.Salesforce+MutationData",
            throwOnError: true)!;
        var fields = mutation
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (property.Name, Id: property.GetCustomAttribute<IdAttribute>()?.Id))
            .OrderBy(field => field.Id)
            .ToArray();

        Assert.Equal(
            [
                ("CommandId", (uint?)0),
                ("Requester", (uint?)1),
                ("AccountId", (uint?)2),
                ("Description", (uint?)3),
                ("Fingerprint", (uint?)4),
                ("UpdateSchemaFingerprint", (uint?)5),
                ("QuerySchemaFingerprint", (uint?)6),
                ("Approval", (uint?)7),
                ("ApprovalEvidence", (uint?)8),
                ("Status", (uint?)9),
            ],
            fields);
    }

    private static void AssertProviderAgnostic(Assembly contracts)
    {
        var leaked = contracts.GetExportedTypes()
            .SelectMany(PublicSurfaceTypes)
            .SelectMany(TypeClosure)
            .Where(type => type.Namespace?.StartsWith("ModelContextProtocol", StringComparison.Ordinal) is true
                || type.Namespace?.StartsWith("System.Text.Json", StringComparison.Ordinal) is true
                || IsDictionary(type))
            .Select(type => type.FullName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(leaked);
    }

    private static IEnumerable<Type> PublicSurfaceTypes(Type type)
    {
        yield return type;

        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (var contract in type.GetInterfaces())
        {
            yield return contract;
        }

        const BindingFlags Members =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.FlattenHierarchy;

        foreach (var constructor in type.GetConstructors(Members).Where(IsVisible))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in type.GetMethods(Members).Where(IsVisible))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            foreach (var argument in method.GetGenericArguments())
            {
                yield return argument;
            }
        }

        foreach (var property in type.GetProperties(Members).Where(IsVisible))
        {
            yield return property.PropertyType;

            foreach (var parameter in property.GetIndexParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var field in type.GetFields(Members).Where(IsVisible))
        {
            yield return field.FieldType;
        }

        foreach (var eventInfo in type.GetEvents(Members).Where(IsVisible))
        {
            if (eventInfo.EventHandlerType is not null)
            {
                yield return eventInfo.EventHandlerType;
            }
        }
    }

    private static bool IsVisible(MethodBase method)
        => method.IsPublic
            || method.IsFamily
            || method.IsFamilyOrAssembly
            || method.IsFamilyAndAssembly;

    private static bool IsVisible(FieldInfo field)
        => field.IsPublic
            || field.IsFamily
            || field.IsFamilyOrAssembly
            || field.IsFamilyAndAssembly;

    private static bool IsVisible(PropertyInfo property)
        => property.GetAccessors(nonPublic: true).Any(IsVisible);

    private static bool IsVisible(EventInfo eventInfo)
        => new[]
        {
            eventInfo.AddMethod,
            eventInfo.RemoveMethod,
            eventInfo.RaiseMethod,
        }
        .OfType<MethodInfo>()
        .Any(IsVisible);

    private static IEnumerable<Type> TypeClosure(Type type)
    {
        var pending = new Stack<Type>([type]);
        var reached = new HashSet<Type>();

        while (pending.TryPop(out var current))
        {
            if (!reached.Add(current))
            {
                continue;
            }

            yield return current;

            if (current.HasElementType && current.GetElementType() is { } element)
            {
                pending.Push(element);
            }

            if (current.BaseType is { } baseType)
            {
                pending.Push(baseType);
            }

            foreach (var contract in current.GetInterfaces())
            {
                pending.Push(contract);
            }

            foreach (var argument in current.GetGenericArguments())
            {
                pending.Push(argument);
            }

            if (current.IsGenericParameter)
            {
                foreach (var constraint in current.GetGenericParameterConstraints())
                {
                    pending.Push(constraint);
                }
            }
        }
    }

    private static bool IsDictionary(Type type) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition() is var definition
        && (definition == typeof(Dictionary<,>)
            || definition == typeof(IDictionary<,>)
            || definition == typeof(IReadOnlyDictionary<,>));
}
