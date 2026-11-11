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

        var gates = source.Current.For("week");
        Assert.Equal(nameof(Hundred.Cap), Assert.Single(gates).Function.Name);
        Assert.True(source.Current.States.ContainsKey("ledger"));
        Assert.Equal(1, source.Version);
    }

    [Fact]
    public void RemoveBumpsVersionAndRaisesChanged()
    {
        var source = new SynapseCatalogSource(Week().Merge(Books()));
        SynapseCatalog? seen = null;
        source.Changed += catalog => seen = catalog;

        source.Remove("ledger");

        Assert.Equal(1, source.Version);
        Assert.Same(source.Current, seen);
        Assert.False(source.Current.States.ContainsKey("ledger"));
        Assert.True(source.Current.States.ContainsKey("week"));
    }

    [Fact]
    public void ForNeedsServesAStagedCatalogWithoutChangingCurrent()
    {
        var source = new SynapseCatalogSource(Week());
        var staged = Week().Without("week").Merge(SynapseCatalog.Scan([typeof(WeekSpend), typeof(Hundred)]));

        source.Stage("upload", staged);

        Assert.Same(staged, source.ForNeeds("week"));
        Assert.Same(source.Current, source.ForNeeds("ledger"));
        Assert.Equal(0, source.Version);
        source.Unstage("upload");
        Assert.Same(source.Current, source.ForNeeds("week"));
    }
}
