using System.Reflection;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ClientApiContracts
{
    [Fact(DisplayName = "Get never takes an owner: owner is ambient")]
    public void GetDoesNotAcceptOwner()
    {
        var gets = typeof(DigitalBrainClient).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == nameof(DigitalBrainClient.Get))
            .ToArray();

        Assert.NotEmpty(gets);

        foreach (var method in gets)
        {
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => parameter.Name is not null
                    && parameter.Name.Contains("owner", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact(DisplayName = "Connect, Get, On and Emit are the programming surface")]
    public void SurfaceIsConnectGetOnEmit()
    {
        var names = typeof(DigitalBrainClient).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(DigitalBrainClient))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(DigitalBrainClient.Connect), names);
        Assert.Contains(nameof(DigitalBrainClient.Get), names);
        Assert.Contains(nameof(DigitalBrainClient.On), names);
        Assert.Contains(nameof(DigitalBrainClient.Emit), names);
    }
}
