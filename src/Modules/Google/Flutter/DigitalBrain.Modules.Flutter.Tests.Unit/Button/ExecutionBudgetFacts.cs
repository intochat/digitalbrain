using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Button.Signals;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Button;

public sealed class ExecutionBudgetFacts
{
    [Fact]
    public async Task DifferentRunBudgetsDoNotLeakBetweenSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var longer = await UnitTest.Create().WithModule<FlutterModule>()
            .WithExecution(new() { AssertionTimeout = TimeSpan.FromSeconds(10) })
            .StartAsync(ct);
        await using var shorter = await UnitTest.Create().WithModule<FlutterModule>()
            .WithExecution(new() { AssertionTimeout = TimeSpan.FromMilliseconds(100) })
            .StartAsync(ct);
        var button = longer.Get<IButton>("longer");
        await button.Set("Go", "clicked");
        await using var longProbe = await longer.Observe<ButtonClicked>(button, ct);
        await using var shortProbe = await shorter.Observe<ButtonClicked>(shorter.Get<IButton>("shorter"), ct);
        var pending = longProbe.NextAsync(ct: ct);
        await Assert.ThrowsAsync<TimeoutException>(() => shortProbe.NextAsync(ct: ct));
        Assert.False(pending.IsCompleted);
        await button.Click();
        Assert.Equal("clicked", (await pending).Action);
    }

    [Fact]
    public async Task ProbeUsesItsRunBudgetInsteadOfFixedFiveSeconds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .WithExecution(new() { AssertionTimeout = TimeSpan.FromMilliseconds(100) })
            .StartAsync(ct);
        await using var probe = await brain.Observe<ButtonClicked>(brain.Get<IButton>("unused"), ct);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(ct);
        guard.CancelAfter(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<TimeoutException>(() => probe.NextAsync(ct: guard.Token));
    }
}