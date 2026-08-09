using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Runtime.Tests;

public sealed class CandidateFamilyIdFacts
{
    [Fact]
    public void CandidateFamilyIdIsOpaqueIdentifierSafeAndRejectsDisplayNames()
    {
        var minted = new CandidateFamilyMinter().Mint();

        Assert.Matches("^cf_[a-z2-7]{26}$", minted.Value);
        Assert.Throws<FormatException>(() => CandidateFamilyId.Parse("owner-a.elon-chart"));
    }

    [Fact]
    public async Task MinterRetriesACollisionBeforeItReservesAFamily()
    {
        var ownerA = new AuthenticatedPrincipal("owner-a");
        var ownerB = new AuthenticatedPrincipal("owner-b");
        var existing = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
        var families = new InMemoryCandidateFamilyRegistry();
        Assert.True(await families.TryReserveAsync(
            ownerA,
            existing,
            TestContext.Current.CancellationToken));
        var minter = new CandidateFamilyMinter(
            new SequenceBase32Source(
                "aaaaaaaaaaaaaaaaaaaaaaaaaa",
                "bbbbbbbbbbbbbbbbbbbbbbbbbb"),
            families);

        var minted = await minter.MintAndReserveAsync(
            ownerB,
            TestContext.Current.CancellationToken);

        Assert.Equal("cf_bbbbbbbbbbbbbbbbbbbbbbbbbb", minted.Value);
        Assert.True(await families.IsReservedForAsync(
            ownerB,
            minted,
            TestContext.Current.CancellationToken));
        Assert.False(await families.IsReservedForAsync(
            ownerA,
            minted,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TrustedFamilyReservationSurvivesRegistryReconstruction()
    {
        await using var run = PocDataRoot.Create(TestPocRoot.Find());
        var ownerA = new AuthenticatedPrincipal("owner-a");
        var ownerB = new AuthenticatedPrincipal("owner-b");
        var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.True(await new FileCandidateFamilyRegistry(run).TryReserveAsync(
            ownerA,
            family,
            TestContext.Current.CancellationToken));
        Assert.False(await new FileCandidateFamilyRegistry(run).TryReserveAsync(
            ownerB,
            family,
            TestContext.Current.CancellationToken));
        Assert.True(await new FileCandidateFamilyRegistry(run).IsReservedForAsync(
            ownerA,
            family,
            TestContext.Current.CancellationToken));
        Assert.False(await new FileCandidateFamilyRegistry(run).IsReservedForAsync(
            ownerB,
            family,
            TestContext.Current.CancellationToken));
    }

    private sealed class SequenceBase32Source(params string[] values) : IBase32Source
    {
        private readonly Queue<string> _values = new(values);

        public string Next(int length)
        {
            var value = _values.Dequeue();
            Assert.Equal(length, value.Length);
            return value;
        }
    }
}
