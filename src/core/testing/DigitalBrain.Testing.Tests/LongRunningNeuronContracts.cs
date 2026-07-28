using System.Reflection;
using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Testing.Tests;

public sealed class LongRunningNeuronContracts
{
    [Theory]
    [InlineData(typeof(INeuron), nameof(INeuron.Deliver))]
    [InlineData(typeof(INeuron), nameof(INeuron.ReadJournal))]
    [InlineData(typeof(ISessionNeuron), nameof(ISessionNeuron.ReadNeuronJournal))]
    public void LongRunningNeuronCallsDeclareProductTimeout(
        Type contract,
        string methodName)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var method = contract.GetMethod(methodName);
        var timeout = method?.GetCustomAttribute<ResponseTimeoutAttribute>();

        Assert.NotNull(timeout);
        Assert.Equal(TimeSpan.FromMinutes(5), timeout.Timeout);
    }
}
