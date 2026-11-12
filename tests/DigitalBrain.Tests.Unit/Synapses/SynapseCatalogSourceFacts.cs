using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.Synapses;

public static class Budget
{
    [Synapse(typeof(Charging), "week")]
    public static WeekSpend WeeklyCap(Charging s, WeekSpend w) => w;
}

public static class Hundred
{
    [Synapse(typeof(Charging), "week")]
    public static WeekSpend Cap(Charging s, WeekSpend w) => w;
}

public sealed class SynapseCatalogSourceFacts
{
    private static SynapseCatalog Week() => SynapseCatalog.Scan([typeof(WeekSpend), typeof(Budget)]);

    private static SynapseCatalog Books() => SynapseCatalog.Scan([typeof(Ledger), typeof(Synapses.Books)]);

    [Fact]
    public void WithoutRemovesTheStateAndItsSynapses()
    {
        var catalog = Week().Merge(Books());

        var without = catalog.Without("week");

        Assert.False(without.States.ContainsKey("week"));
        Assert.Empty(without.For("week"));
        Assert.True(without.States.ContainsKey("ledger"));
        Assert.Equal(catalog.For("ledger").Count, without.For("ledger").Count);
        Assert.True(catalog.States.ContainsKey("week"));
    }

    [Fact]
    public void ReplaceDropsTheOldAliasAndKeepsOthers()
    {
        var source = new SynapseCatalogSource(Week().Merge(Books()));
        var replacement = SynapseCatalog.Scan([typeof(WeekSpend), typeof(Hundred)]);

        source.Replace("week", replacement);

        var gates = source.Static.For("week");
        Assert.Equal(nameof(Hundred.Cap), Assert.Single(gates).Function.Name);
        Assert.True(source.Static.States.ContainsKey("ledger"));
    }

    [Fact]
    public void RemoveDropsTheAliasAndKeepsOthers()
    {
        var source = new SynapseCatalogSource(Week().Merge(Books()));

        source.Remove("ledger");

        Assert.False(source.Static.States.ContainsKey("ledger"));
        Assert.True(source.Static.States.ContainsKey("week"));
    }

    [Fact]
    public void OfReturnsLoaderCatalogOrStatic()
    {
        var source = new SynapseCatalogSource(Week());
        Assert.Same(source.Static, source.Of(typeof(WeekSpend)));
    }
}
