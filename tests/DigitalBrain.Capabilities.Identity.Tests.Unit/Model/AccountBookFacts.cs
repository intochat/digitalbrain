namespace DigitalBrain.Capabilities.Identity.Tests.Unit.Model;

using DigitalBrain.Capabilities.Identity.Model;
using Xunit;

public sealed class AccountBookFacts
{
    [Fact]
    public void EnsureAccountIsIdempotentAndEmitsAccountEnsured()
    {
        var book = new AccountBook();
        var now = DateTimeOffset.UtcNow;

        var (next, account, facts) = book.EnsureAccount("acc-1", "Account 1", "p-1", now, now);
        Assert.Equal("acc-1", account.AccountId);
        Assert.Single(facts);
        var signal = Assert.IsType<AccountEnsured>(facts[0]);
        Assert.Equal("acc-1", signal.Account.AccountId);

        var (next2, account2, facts2) = next.EnsureAccount("acc-1", "Account 1", "p-1", now, now);
        Assert.Empty(facts2);
        Assert.Equal(account.AccountId, account2.AccountId);
        Assert.Same(next, next2);
    }

    [Fact]
    public void CreateBrainOperationIsScopedToAccountPreventingHijack()
    {
        var book = new AccountBook();
        var now = DateTimeOffset.UtcNow;

        var (b1, _, _) = book.EnsureAccount("acc-1", "Account 1", "p-1", now, now);
        var (b2, _, _) = b1.EnsureAccount("acc-2", "Account 2", "p-2", now, now);

        const string sharedOp = "shared-op-123";

        var (bookWithBrain1, prov1, facts1) = b2.CreateBrain("acc-1", "p-1", sharedOp, "Brain 1", BrainKind.Circle, now);
        Assert.True(prov1.NewBrain);
        Assert.NotEmpty(facts1);

        // Account 2 using the same op gets its own distinct brain, not account 1's brain
        var (bookWithBrain2, prov2, facts2) = bookWithBrain1.CreateBrain("acc-2", "p-2", sharedOp, "Brain 2", BrainKind.Circle, now);
        Assert.True(prov2.NewBrain);
        Assert.NotEmpty(facts2);
        Assert.NotEqual(prov1.Member.BrainId, prov2.Member.BrainId);
        Assert.Equal("acc-1", prov1.Member.AccountId);
        Assert.Equal("acc-2", prov2.Member.AccountId);

        // Account 1 repeating the same op gets the original brain
        var (bookRepeat, provRepeat, factsRepeat) = bookWithBrain2.CreateBrain("acc-1", "p-1", sharedOp, "Brain 1", BrainKind.Circle, now);
        Assert.False(provRepeat.NewBrain);
        Assert.Empty(factsRepeat);
        Assert.Equal(prov1.Member.BrainId, provRepeat.Member.BrainId);
    }
}
