using System.Reflection;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class CodeWorkspaceNeuronFacts
{
    [Fact]
    public void Every_contract_method_uses_section_7_types()
    {
        var options = DescriptorTable.ContractOptions(CodingJson.Default);
        var methods = typeof(ICodeWorkspace).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            var parameterTypes = method.GetParameters()
                .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
                .Select(parameter => parameter.ParameterType)
                .ToArray();

            if (parameterTypes.Length == 1)
            {
                DescriptorRules.ValidateMemberTypes(method, options.GetTypeInfo(parameterTypes[0]));
            }

            var returnType = method.ReturnType.GetGenericArguments()[0];
            DescriptorRules.ValidateMemberTypes(method, options.GetTypeInfo(returnType));
        }

        Assert.True(methods.Length >= 7, $"Expected at least seven declared methods on {nameof(ICodeWorkspace)}, found {methods.Length}.");
    }
}
