using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class IntegrationContracts
{
    [Fact(DisplayName = "Gmail exposes semantic message reading without MCP-shaped public types")]
    public void GmailContractIsSemanticAndProviderAgnostic()
    {
        Assert.Contains(typeof(INeuron), typeof(IGmail).GetInterfaces());

        var read = Assert.Single(typeof(IGmail).GetMethods());
        Assert.Equal(nameof(IGmail.ReadMessageAsync), read.Name);
        Assert.Equal(typeof(Task<GmailMessage>), read.ReturnType);
        Assert.Equal([typeof(string)], read.GetParameters().Select(parameter => parameter.ParameterType));

        AssertProviderAgnostic(typeof(IGmail).Assembly);
    }

    [Fact(DisplayName = "Salesforce exposes an exact two-phase Account mutation contract")]
    public void SalesforceContractRequiresBoundApprovalAndModelsUncertainty()
    {
        Assert.Contains(typeof(INeuron), typeof(ISalesforce).GetInterfaces());

        var methods = typeof(ISalesforce).GetMethods()
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [nameof(ISalesforce.ApproveAccountDescriptionAsync), nameof(ISalesforce.ProposeAccountDescriptionAsync)],
            methods.Select(method => method.Name));
        Assert.All(methods, method =>
            Assert.Equal(typeof(Task<SalesforceAccountDescriptionMutation>), method.ReturnType));
        Assert.Equal(
            [typeof(CommandId), typeof(string)],
            methods[0].GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [typeof(CommandId), typeof(string), typeof(string)],
            methods[1].GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [
                SalesforceMutationState.AwaitingApproval,
                SalesforceMutationState.Completed,
                SalesforceMutationState.OutcomeUncertain,
            ],
            Enum.GetValues<SalesforceMutationState>());

        AssertProviderAgnostic(typeof(ISalesforce).Assembly);
    }

    private static void AssertProviderAgnostic(Assembly contracts)
    {
        var leaked = contracts.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType))
            .SelectMany(Flatten)
            .Where(type => type.Namespace?.StartsWith("ModelContextProtocol", StringComparison.Ordinal) is true
                || type.Namespace?.StartsWith("System.Text.Json", StringComparison.Ordinal) is true
                || IsDictionary(type))
            .Select(type => type.FullName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(leaked);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        foreach (var argument in type.GenericTypeArguments)
        {
            foreach (var nested in Flatten(argument))
            {
                yield return nested;
            }
        }
    }

    private static bool IsDictionary(Type type) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition() is var definition
        && (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>));
}
