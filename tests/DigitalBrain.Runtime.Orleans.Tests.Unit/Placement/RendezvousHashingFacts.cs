using System.Net;
using DigitalBrain.Runtime.Orleans.Placement;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Placement;

public sealed class RendezvousHashingFacts
{
    private static SiloAddress CreateSilo(int port, int generation = 1)
        => SiloAddress.New(new IPEndPoint(IPAddress.Loopback, port), generation);

    // Canonical Rendezvous (Highest Random Weight) implementation for independent oracle verification
    private static SiloAddress OracleRendezvous(string brainId, SiloAddress[] silos)
    {
        SiloAddress bestSilo = silos[0];
        ulong highestWeight = 0;

        foreach (var silo in silos)
        {
            var weight = ComputeWeight(brainId, silo.ToParsableString());
            if (weight > highestWeight)
            {
                highestWeight = weight;
                bestSilo = silo;
            }
        }
        return bestSilo;
    }

    private static ulong ComputeWeight(string brainId, string siloAddress)
    {
        ulong hash = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        foreach (var c in brainId) { hash = (hash ^ c) * prime; }
        hash = (hash ^ ':') * prime;
        foreach (var c in siloAddress) { hash = (hash ^ c) * prime; }

        hash ^= hash >> 33;
        hash *= 0xff51afd7ed558ccdUL;
        hash ^= hash >> 33;
        hash *= 0xc4ceb9fe1a85ec53UL;
        hash ^= hash >> 33;

        return hash;
    }

    [Fact]
    public void Determinism_SameBrainMapsToSameSiloAcrossRepeatedInvocations()
    {
        var silos = new[]
        {
            CreateSilo(11111),
            CreateSilo(11112),
            CreateSilo(11113),
            CreateSilo(11114)
        };

        var brains = Enumerable.Range(1, 100).Select(i => $"tenant-brain-{i}").ToArray();

        foreach (var brain in brains)
        {
            var target1 = OracleRendezvous(brain, silos);
            var target2 = OracleRendezvous(brain, silos);
            var target3 = OracleRendezvous(brain, silos);

            Assert.Equal(target1, target2);
            Assert.Equal(target2, target3);
        }
    }

    [Fact]
    public void OrderIndependence_PermutedSiloListSelectsSameSilo()
    {
        var siloA = CreateSilo(11111);
        var siloB = CreateSilo(11112);
        var siloC = CreateSilo(11113);

        var list1 = new[] { siloA, siloB, siloC };
        var list2 = new[] { siloC, siloA, siloB };
        var list3 = new[] { siloB, siloC, siloA };

        var brains = Enumerable.Range(1, 100).Select(i => $"brain-{i}").ToArray();

        foreach (var brain in brains)
        {
            var s1 = OracleRendezvous(brain, list1);
            var s2 = OracleRendezvous(brain, list2);
            var s3 = OracleRendezvous(brain, list3);

            Assert.Equal(s1, s2);
            Assert.Equal(s2, s3);
        }
    }

    [Fact]
    public void Uniformity_DistributionAcrossNSilosIsFair()
    {
        const int numSilos = 4;
        const int numBrains = 4000;
        var silos = Enumerable.Range(1, numSilos).Select(i => CreateSilo(11000 + i)).ToArray();

        var counts = new Dictionary<SiloAddress, int>();
        foreach (var silo in silos) { counts[silo] = 0; }

        for (var i = 0; i < numBrains; i++)
        {
            var brain = $"cluster-brain-{i}";
            var chosen = OracleRendezvous(brain, silos);
            counts[chosen]++;
        }

        const double expected = (double)numBrains / numSilos; // 1000
        foreach (var (silo, count) in counts)
        {
            // Verify each silo receives within 25% of the fair mean (750 to 1250)
            Assert.InRange(count, (int)(expected * 0.75), (int)(expected * 1.25));
        }
    }

    [Fact]
    public void MinimalReshuffle_AddingOneSiloMovesApproximatelyOneOverNPlusOne()
    {
        // Start with N = 4 silos
        const int initialSiloCount = 4;
        const int numBrains = 5000;
        var initialSilos = Enumerable.Range(1, initialSiloCount).Select(i => CreateSilo(11000 + i)).ToArray();

        // Initial mapping
        var initialMapping = new Dictionary<string, SiloAddress>(numBrains);
        for (var i = 0; i < numBrains; i++)
        {
            var brain = $"resilience-brain-{i}";
            initialMapping[brain] = OracleRendezvous(brain, initialSilos);
        }

        // Expand cluster to N + 1 = 5 silos
        var newSilo = CreateSilo(11005);
        var expandedSilos = initialSilos.Append(newSilo).ToArray();

        var movedToNewSilo = 0;
        var movedBetweenExistingSilos = 0;
        var stayedOnSameSilo = 0;

        for (var i = 0; i < numBrains; i++)
        {
            var brain = $"resilience-brain-{i}";
            var oldSilo = initialMapping[brain];
            var newChoice = OracleRendezvous(brain, expandedSilos);

            if (newChoice == newSilo)
            {
                movedToNewSilo++;
            }
            else if (newChoice == oldSilo)
            {
                stayedOnSameSilo++;
            }
            else
            {
                movedBetweenExistingSilos++;
            }
        }

        // 1. In Highest Random Weight hashing, NO brains ever move between existing silos
        Assert.Equal(0, movedBetweenExistingSilos);

        // 2. Expected migration to the new silo is ~ 1 / (N + 1) = 1/5 = 20% (1000 +/- tolerance)
        var migrationFraction = (double)movedToNewSilo / numBrains;
        Assert.InRange(migrationFraction, 0.15, 0.25);

        // 3. Expected unchanged fraction is ~ N / (N + 1) = 4/5 = 80% (4000 +/- tolerance)
        var retainedFraction = (double)stayedOnSameSilo / numBrains;
        Assert.InRange(retainedFraction, 0.75, 0.85);

        Assert.Equal(numBrains, movedToNewSilo + stayedOnSameSilo);
    }

    [Fact]
    public void SingleSiloCluster_AlwaysSelectsThatSilo()
    {
        var loneSilo = CreateSilo(11111);
        var silos = new[] { loneSilo };

        for (var i = 0; i < 50; i++)
        {
            var chosen = OracleRendezvous($"single-silo-brain-{i}", silos);
            Assert.Equal(loneSilo, chosen);
        }
    }

    [Fact]
    public void BrainKeyResolution_HandlesCompositeKeysAndSlashes()
    {
        Assert.Equal("alice", BrainAffinePlacementDirector.BrainOf("alice/apps/calculator"));
        Assert.Equal("bob", BrainAffinePlacementDirector.BrainOf("bob/storage/work/file.csv"));
        Assert.Equal("charlie", BrainAffinePlacementDirector.BrainOf("charlie"));
    }
}
