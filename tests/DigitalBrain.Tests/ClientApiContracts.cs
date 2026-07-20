using System.Reflection;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ClientApiContracts
{
    [Fact(DisplayName = "DigitalBrainClient is the package's only public client facade")]
    public void ThereIsOneClientFacade()
    {
        var facades = typeof(DigitalBrainClient).Assembly
            .GetExportedTypes()
            .Where(type => type.IsClass)
            .ToArray();

        Assert.Equal([typeof(DigitalBrainClient)], facades);
    }

    [Fact(DisplayName = "Send never takes an owner: owner is ambient")]
    public void SendDoesNotAcceptOwner()
    {
        var sends = typeof(DigitalBrainClient).GetMethods(BindingFlags.Instance | BindingFlags.Public)
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

    [Fact(DisplayName = "the client exposes only owner-bound session entry points")]
    public void SurfaceIsConnectSendAndEmit()
    {
        var names = typeof(DigitalBrainClient).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(DigitalBrainClient))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            ["Connect", "EmitAsync", "SendAsync", "get_Owner"],
            names.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact(DisplayName = "the client never returns raw neuron proxies")]
    public void SurfaceReturnsNoNeuronProxy()
    {
        var methods = typeof(DigitalBrainClient).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(DigitalBrainClient));

        Assert.DoesNotContain(
            methods,
            method => typeof(Orleans.IGrain).IsAssignableFrom(method.ReturnType));
    }
}
