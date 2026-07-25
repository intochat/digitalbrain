using System.Reflection;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class AiContractBoundaries
{
    [Fact(DisplayName = "ILLM does not inherit IAgent")]
    public void IllmDoesNotInheritIAgent()
        => Assert.False(typeof(IAgent).IsAssignableFrom(typeof(ILLM)));

    [Fact(DisplayName = "every concrete LLM follows namespace, contract, and typed-key grammar")]
    public void EveryConcreteLlmFollowsNamespaceContractAndTypedKeyGrammar()
    {
        var models = ConcreteLlms().ToArray();
        Assert.NotEmpty(models);

        Assert.All(models, model =>
        {
            Assert.True(model.IsSealed, $"{model.FullName} must be sealed.");

            Assert.False(
                string.IsNullOrEmpty(model.Namespace),
                $"{model.FullName} must declare a namespace.");
            Assert.StartsWith("DigitalBrain.AI.", model.Namespace, StringComparison.Ordinal);
            Assert.NotEqual("DigitalBrain.AI", model.Namespace);

            var contract = typeof(ILLM).Assembly
                .GetExportedTypes()
                .SingleOrDefault(type =>
                    type.IsInterface
                    && type.Name == "I" + model.Name
                    && type.Namespace == model.Namespace);

            Assert.True(
                contract is not null,
                $"{model.FullName} must implement I{model.Name} in namespace '{model.Namespace}'.");
            Assert.True(
                typeof(ILLM).IsAssignableFrom(contract),
                $"{contract!.FullName} must extend ILLM.");
            Assert.Contains(contract, model.GetInterfaces());

            var chatClientParameters = model
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => parameter.ParameterType == typeof(IChatClient))
                .ToArray();

            Assert.NotEmpty(chatClientParameters);
            Assert.All(chatClientParameters, parameter =>
            {
                var llmKey = parameter
                    .GetCustomAttributes(inherit: false)
                    .SingleOrDefault(attribute =>
                        attribute.GetType() is { IsGenericType: true } attributeType
                        && attributeType.GetGenericTypeDefinition() == typeof(LlmAttribute<>));

                Assert.True(
                    llmKey is not null,
                    $"{model.FullName} must key IChatClient with [Llm<{model.Name}>].");
                Assert.Equal(model, llmKey!.GetType().GetGenericArguments()[0]);

                var keyed = Assert.IsAssignableFrom<FromKeyedServicesAttribute>(llmKey);
                Assert.Equal(model, keyed.Key);
            });
        });
    }

    [Fact(DisplayName = "IChatClient injection stays confined to concrete LLM neurons")]
    public void IChatClientInjectionStaysConfinedToConcreteLlmNeurons()
    {
        var offenders = new[]
            {
                typeof(Neuron).Assembly,
                typeof(ILLM).Assembly,
                typeof(LLM).Assembly,
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters()
                    .Where(parameter => parameter.ParameterType == typeof(IChatClient))
                    .Select(_ => type)))
            .Distinct()
            .Where(type => !type.IsSubclassOf(typeof(LLM)))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> ConcreteLlms() =>
        typeof(LLM).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.IsSubclassOf(typeof(LLM)));
}
