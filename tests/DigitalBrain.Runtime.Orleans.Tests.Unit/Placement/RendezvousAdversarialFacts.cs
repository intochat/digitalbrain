using System.Net;
using DigitalBrain.Runtime.Orleans.Placement;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Placement;

public sealed class RendezvousAdversarialFacts
{
    private static SiloAddress CreateSilo(int port, int generation = 1)
        => SiloAddress.New(new IPEndPoint(IPAddress.Loopback, port), generation);

    private static SiloAddress SelectSilo(string brainId, SiloAddress[] silos)
    {
        SiloAddress bestSilo = silos[0];
        ulong highestWeight = 0;

        foreach (var silo in silos)
        {
            var weight = BrainAffinePlacementDirector.ComputeWeight(brainId, silo.ToParsableString());
            if (weight > highestWeight)
            {
                highestWeight = weight;
                bestSilo = silo;
            }
        }
        return bestSilo;
    }

    [Fact]
    public void DeterminismStressTest_10000Keys_Across10Runs_AlwaysIdentical()
    {
        const int keyCount = 10_000;
        var silos = new[]
        {
            CreateSilo(11101),
            CreateSilo(11102),
            CreateSilo(11103),
            CreateSilo(11104),
            CreateSilo(11105)
        };

        var keys = Enumerable.Range(0, keyCount).Select(i => $"stress-brain-{i:D8}").ToArray();

        // Baseline run
        var baseline = new SiloAddress[keyCount];
        for (var i = 0; i < keyCount; i++)
        {
            baseline[i] = SelectSilo(keys[i], silos);
        }

        // 10 subsequent runs
        for (var run = 1; run <= 10; run++)
        {
            for (var i = 0; i < keyCount; i++)
            {
                var chosen = SelectSilo(keys[i], silos);
                Assert.Equal(baseline[i], chosen);
            }
        }
    }

    [Fact]
    public void Determinism_PermutedSiloList_SelectsSameSiloFor10000Keys()
    {
        const int keyCount = 10_000;
        var silo1 = CreateSilo(11101);
        var silo2 = CreateSilo(11102);
        var silo3 = CreateSilo(11103);
        var silo4 = CreateSilo(11104);

        var listA = new[] { silo1, silo2, silo3, silo4 };
        var listB = new[] { silo4, silo2, silo1, silo3 };
        var listC = new[] { silo3, silo1, silo4, silo2 };

        for (var i = 0; i < keyCount; i++)
        {
            var brain = $"perm-brain-{i:D8}";
            var choiceA = SelectSilo(brain, listA);
            var choiceB = SelectSilo(brain, listB);
            var choiceC = SelectSilo(brain, listC);

            Assert.Equal(choiceA, choiceB);
            Assert.Equal(choiceA, choiceC);
        }
    }

    [Fact]
    public void DistributionUniformity_3Silos_10000Keys_UniformSpread()
    {
        const int siloCount = 3;
        const int keyCount = 10_000;
        var silos = Enumerable.Range(1, siloCount).Select(i => CreateSilo(11100 + i)).ToArray();

        var counts = silos.ToDictionary(s => s, _ => 0);
        for (var i = 0; i < keyCount; i++)
        {
            var brain = $"uniformity3-brain-{i:D8}";
            var chosen = SelectSilo(brain, silos);
            counts[chosen]++;
        }

        const double expected = (double)keyCount / siloCount; // 3333.33
        // Verify each silo receives within 15% of expected mean (2833 to 3833)
        foreach (var (silo, count) in counts)
        {
            Assert.InRange(count, (int)(expected * 0.85), (int)(expected * 1.15));
        }

        // Pearson Chi-Square test: chi2 = sum((O - E)^2 / E)
        // With df = 2, critical value at p=0.001 is 13.82
        var chi2 = counts.Values.Sum(c => Math.Pow(c - expected, 2) / expected);
        Assert.True(chi2 < 20.0, $"Chi-square value {chi2} indicates non-uniform distribution across 3 silos.");
    }

    [Fact]
    public void DistributionUniformity_5Silos_10000Keys_UniformSpread()
    {
        const int siloCount = 5;
        const int keyCount = 10_000;
        var silos = Enumerable.Range(1, siloCount).Select(i => CreateSilo(11100 + i)).ToArray();

        var counts = silos.ToDictionary(s => s, _ => 0);
        for (var i = 0; i < keyCount; i++)
        {
            var brain = $"uniformity5-brain-{i:D8}";
            var chosen = SelectSilo(brain, silos);
            counts[chosen]++;
        }

        const double expected = (double)keyCount / siloCount; // 2000
        // Verify each silo receives within 18% of expected mean (1640 to 2360)
        foreach (var (silo, count) in counts)
        {
            Assert.InRange(count, (int)(expected * 0.82), (int)(expected * 1.18));
        }

        // Pearson Chi-Square test: df = 4, critical value at p=0.001 is 18.47
        var chi2 = counts.Values.Sum(c => Math.Pow(c - expected, 2) / expected);
        Assert.True(chi2 < 25.0, $"Chi-square value {chi2} indicates non-uniform distribution across 5 silos.");
    }

    [Fact]
    public void DistributionUniformity_10Silos_10000Keys_UniformSpread()
    {
        const int siloCount = 10;
        const int keyCount = 10_000;
        var silos = Enumerable.Range(1, siloCount).Select(i => CreateSilo(11100 + i)).ToArray();

        var counts = silos.ToDictionary(s => s, _ => 0);
        for (var i = 0; i < keyCount; i++)
        {
            var brain = $"uniformity10-brain-{i:D8}";
            var chosen = SelectSilo(brain, silos);
            counts[chosen]++;
        }

        const double expected = (double)keyCount / siloCount; // 1000
        // Verify each silo receives within 25% of expected mean (750 to 1250)
        foreach (var (silo, count) in counts)
        {
            Assert.InRange(count, (int)(expected * 0.75), (int)(expected * 1.25));
        }

        // Pearson Chi-Square test: df = 9, critical value at p=0.001 is 27.88
        var chi2 = counts.Values.Sum(c => Math.Pow(c - expected, 2) / expected);
        Assert.True(chi2 < 35.0, $"Chi-square value {chi2} indicates non-uniform distribution across 10 silos.");
    }

    [Fact]
    public void MinimalDisruption_Scaling3To4Silos_ExactlyZeroMigrationBetweenExistingSilos_AndApproximately25PercentMigrationToNewSilo()
    {
        const int keyCount = 10_000;
        var silo1 = CreateSilo(11101);
        var silo2 = CreateSilo(11102);
        var silo3 = CreateSilo(11103);
        var initialSilos = new[] { silo1, silo2, silo3 };

        // 1. Initial assignment across 3 silos
        var initialMapping = new Dictionary<string, SiloAddress>(keyCount);
        for (var i = 0; i < keyCount; i++)
        {
            var key = $"disruption-brain-{i:D8}";
            initialMapping[key] = SelectSilo(key, initialSilos);
        }

        // 2. Scale from 3 to 4 silos
        var silo4 = CreateSilo(11104);
        var expandedSilos = new[] { silo1, silo2, silo3, silo4 };

        var movedToNewSilo = 0;
        var movedBetweenExistingSilos = 0;
        var stayedOnSameSilo = 0;

        for (var i = 0; i < keyCount; i++)
        {
            var key = $"disruption-brain-{i:D8}";
            var oldSilo = initialMapping[key];
            var newSilo = SelectSilo(key, expandedSilos);

            if (newSilo == silo4)
            {
                movedToNewSilo++;
            }
            else if (newSilo == oldSilo)
            {
                stayedOnSameSilo++;
            }
            else
            {
                // CRITICAL INVARIANT VIOLATION: A key jumped between existing silos!
                movedBetweenExistingSilos++;
            }
        }

        // 3. Mathematical assertions:
        // Invariant 1: Exactly ZERO keys must ever migrate between existing silos 1, 2, 3!
        Assert.Equal(0, movedBetweenExistingSilos);

        // Invariant 2: Migration fraction to the new silo 4 must be approximately 1/(N+1) = 1/4 = 25%
        // Statistical confidence interval for N=10,000, p=0.25 is [0.22, 0.28] (3-sigma: 2500 +/- 130)
        var migrationFraction = (double)movedToNewSilo / keyCount;
        Assert.InRange(migrationFraction, 0.22, 0.28);

        // Invariant 3: Retention fraction on same silo must be approximately N/(N+1) = 3/4 = 75%
        var retainedFraction = (double)stayedOnSameSilo / keyCount;
        Assert.InRange(retainedFraction, 0.72, 0.78);

        // Invariant 4: Total keys accounted for
        Assert.Equal(keyCount, movedToNewSilo + stayedOnSameSilo);
    }

    [Theory]
    [InlineData("alice", "alice")]
    [InlineData("alice/apps/calculator", "alice")]
    [InlineData("tenant-1/apps/ino/editor/v1", "tenant-1")]
    [InlineData("brain-99/storage/work/projects/2026/src/code.cs", "brain-99")]
    [InlineData("@system/registry/settings", "@system")]
    [InlineData("plain-brain-without-slash", "plain-brain-without-slash")]
    public void BrainAffinePlacementDirector_BrainOf_ExtractsCorrectBrainUnderComplexGrainKeys(string grainKey, string expectedBrain)
    {
        var extracted = BrainAffinePlacementDirector.BrainOf(grainKey);
        Assert.Equal(expectedBrain, extracted);
    }

    [Fact]
    public void BrainAffinePlacementDirector_BrainOf_DoesNotCrashOnWhitespaceKey()
    {
        var exception = Record.Exception(() =>
        {
            var brain = BrainAffinePlacementDirector.BrainOf("alice/apps/   ");
            Assert.Equal("alice", brain);
        });

        Assert.Null(exception);
    }
}
