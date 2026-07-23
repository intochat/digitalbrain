using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.Tests;

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
            .Where(method => method.Name == "SendAsync")
            .ToArray();

        Assert.NotEmpty(sends);

        foreach (var method in sends)
        {
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => parameter.Name is not null
                    && parameter.Name.Contains("owner", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact(DisplayName = "the client exposes Connect, Get, Send, and Emit")]
    public void SurfaceIsConnectGetSendAndEmit()
    {
        var names = typeof(DigitalBrainClient).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(DigitalBrainClient))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            ["Connect", "EmitAsync", "Get", "SendAsync", "get_Owner"],
            names.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact(DisplayName = "Get is constrained to neuron contracts")]
    public void GetIsNeuronConstrained()
    {
        var get = typeof(IDigitalBrain).GetMethods()
            .Single(method => method.Name == "Get" && method.GetParameters().Length == 1);

        var constraint = get.GetGenericArguments().Single().GetGenericParameterConstraints();

        Assert.Contains(typeof(INeuron), constraint);
    }
}
