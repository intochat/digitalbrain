using System.Reflection;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AIContracts
{
    private static readonly Assembly Runtime = typeof(AIModule).Assembly;

    [Fact(DisplayName = "model identity is expressed by its namespace and concrete neuron type")]
    public void ModelIdentityIsTheType()
    {
        Assert.Equal("DigitalBrain.AI.Ollama", typeof(Llama32).Namespace);
        Assert.Equal("DigitalBrain.AI.OpenAI", typeof(Gpt56).Namespace);
        Assert.Equal(typeof(LLM), typeof(Llama32).BaseType);
        Assert.Equal(typeof(LLM), typeof(Gpt56).BaseType);
        Assert.Contains(typeof(ILlama32), typeof(Llama32).GetInterfaces());
        Assert.Contains(typeof(IGpt56), typeof(Gpt56).GetInterfaces());
    }

    [Fact(DisplayName = "a concrete LLM receives only the chat client keyed by its own type")]
    public void ModelConstructorCarriesItsTypedLlmKey()
    {
        var parameter = Assert.Single(Assert.Single(typeof(Llama32).GetConstructors()).GetParameters());
        var binding = Assert.IsAssignableFrom<FromKeyedServicesAttribute>(
            Assert.Single(parameter.GetCustomAttributes(inherit: false)));

        Assert.Equal(typeof(IChatClient), parameter.ParameterType);
        Assert.Equal(typeof(Llama32), binding.Key);
    }

    [Fact(DisplayName = "AIModule registers each chat client by its concrete LLM neuron type")]
    public void ModuleRegistersTypedChatClients()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseOrleans(AIModule.Configure);

        using var host = builder.Build();
        var llama = host.Services.GetRequiredKeyedService<IChatClient>(typeof(Llama32));

        Assert.NotNull(llama);
        Assert.Throws<InvalidOperationException>(
            () => host.Services.GetRequiredKeyedService<IChatClient>(typeof(Gpt56)));
    }

    [Fact(DisplayName = "IChatClient injection is confined to concrete LLM neurons")]
    public void ChatClientInjectionIsConfinedToConcreteModels()
    {
        var consumers = Runtime.GetTypes()
            .Where(type => type.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(IChatClient)))
            .ToArray();

        Assert.Equal([typeof(Gpt56), typeof(Llama32)], consumers.OrderBy(type => type.Name));
        Assert.All(consumers, type => Assert.Equal(typeof(LLM), type.BaseType));
    }

    [Fact(DisplayName = "every concrete LLM follows the namespace, contract and typed-key grammar")]
    public void ConcreteModelsFollowTheTypeGrammar()
    {
        var models = Runtime.GetExportedTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsSubclassOf(typeof(LLM)))
            .ToArray();

        Assert.NotEmpty(models);

        foreach (var model in models)
        {
            Assert.StartsWith("DigitalBrain.AI.", model.Namespace, StringComparison.Ordinal);
            Assert.Contains(
                model.GetInterfaces(),
                contract => contract.Namespace == model.Namespace
                    && contract.Name == $"I{model.Name}"
                    && typeof(ILLM).IsAssignableFrom(contract));

            var parameter = Assert.Single(Assert.Single(model.GetConstructors()).GetParameters());
            var binding = Assert.Single(
                parameter.GetCustomAttributes(inherit: false).OfType<FromKeyedServicesAttribute>());

            Assert.Equal(typeof(IChatClient), parameter.ParameterType);
            Assert.Equal(model, binding.Key);
        }
    }

    [Fact(DisplayName = "AI contracts do not expose or reference Microsoft.Extensions.AI")]
    public void ContractsRemainProviderAgnostic()
    {
        var contracts = typeof(ILLM).Assembly;

        Assert.DoesNotContain(
            contracts.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal) is true);
        Assert.DoesNotContain(
            contracts.GetExportedTypes().SelectMany(type => type.GetMembers()),
            member => member.ToString()?.Contains("Microsoft.Extensions.AI", StringComparison.Ordinal) is true);
    }
}
