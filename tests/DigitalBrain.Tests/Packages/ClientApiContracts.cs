using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.Tests.Packages;

public sealed class ClientApiContracts
{
    [Fact(DisplayName = "DigitalBrainClient is the package's only public client class")]
    public void ThereIsOneClientClass()
    {
        var classes = typeof(DigitalBrainClient).Assembly
            .GetExportedTypes()
            .Where(type => type.IsClass)
            .ToArray();

        Assert.Equal([typeof(DigitalBrainClient)], classes);
    }

    [Fact(DisplayName = "IDigitalBrain is the public client contract")]
    public void ClientContractIsIDigitalBrain()
    {
        Assert.Contains(typeof(IDigitalBrain), typeof(DigitalBrainClient).Assembly.GetExportedTypes());
        Assert.Contains(typeof(IDigitalBrain), typeof(DigitalBrainClient).GetInterfaces());
    }

    [Fact(DisplayName = "Send never takes an owner: owner is ambient")]
    public void SendDoesNotAcceptOwner()
    {
        var sends = typeof(IDigitalBrain).GetMethods()
            .Where(method => method.Name == nameof(IDigitalBrain.SendAsync))
            .ToArray();

        Assert.NotEmpty(sends);

        foreach (var method in sends)
        {
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => parameter.Name is not null
                    && parameter.Name.Contains(nameof(IDigitalBrain.Owner), StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact(DisplayName = "the client exposes Connect, Get, Send, and Emit")]
    public void SurfaceIsConnectGetSendAndEmit()
    {
        var methods = typeof(DigitalBrainClient)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(DigitalBrainClient) && !method.IsSpecialName)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(DigitalBrainClient.Connect),
                nameof(DigitalBrainClient.EmitAsync),
                nameof(DigitalBrainClient.Get),
                nameof(DigitalBrainClient.SendAsync),
            ],
            methods);
        Assert.NotNull(typeof(DigitalBrainClient).GetProperty(nameof(DigitalBrainClient.Owner)));
    }

    [Fact(DisplayName = "Get is constrained to neuron contracts")]
    public void GetIsNeuronConstrained()
    {
        var get = typeof(IDigitalBrain).GetMethods()
            .Single(method => method.Name == nameof(IDigitalBrain.Get) && method.GetParameters().Length == 1);

        var constraint = get.GetGenericArguments().Single().GetGenericParameterConstraints();

        Assert.Contains(typeof(INeuron), constraint);
    }
}
