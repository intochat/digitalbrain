using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GrainTypeNamingContracts
{
    private static readonly OwnerId Owner = new("acme");

    [Theory]
    [InlineData(typeof(Ledger), "Ledger")]
    [InlineData(typeof(LedgerGrain), "Ledger")]
    [InlineData(typeof(Basket), "cart")]
    [InlineData(typeof(Grain), "Grain")]
    public void GrainTypeNameFollowsTheRuleOrleansUses(Type neuronType, string expected)
        => Assert.Equal(expected, NeuronId.GrainTypeNameOf(neuronType));

    [Theory]
    [InlineData(typeof(Ledger), "ledger")]
    [InlineData(typeof(LedgerGrain), "ledger")]
    [InlineData(typeof(Basket), "cart")]
    public void AnAddressBuiltFromTheClrTypeMatchesTheGrainTypeOrleansResolves(Type neuronType, string expected)
        => Assert.Equal(expected, new NeuronId(NeuronId.GrainTypeNameOf(neuronType), Owner, "main").Type);

    private sealed class Ledger;

    private sealed class LedgerGrain;

    [GrainType("cart")]
    private sealed class Basket;

    private sealed class Grain;
}
