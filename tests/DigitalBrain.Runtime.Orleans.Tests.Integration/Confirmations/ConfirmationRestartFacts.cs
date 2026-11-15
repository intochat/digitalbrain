using DigitalBrain.Confirmation;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Confirmations;

public sealed class ConfirmationRestartFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task PendingConfirmationExpiresViaDurableReminderAfterSiloRestart()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = new BrainId("cf-restart");
        var id = IConfirmationRequests.Of(brain);
        var requests = NeuronHosting.Get<IConfirmationRequests>(silo.Services, id);
        var plan = new ConfirmationPlan(
            "cf-restart-exp",
            ConfirmationAction.Delete,
            Destructive: true,
            "Delete item?",
            "Permanent delete",
            "Delete",
            "Keep",
            "item.txt")
        {
            Expiry = TimeSpan.FromSeconds(3),
        };

        Assert.Null(await requests.Request(plan));
        Assert.Null(await requests.Outcome(plan.Id));

        await silo.Restart(ct);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(60));
        ConfirmationResolved? expired = null;
        while (expired is null)
        {
            var page = await silo.History(brain.Value).Read(id, 0, 20, limit.Token);
            expired = page.Signals.Select(entry => entry.Signal).OfType<ConfirmationResolved>()
                .FirstOrDefault(signal => signal.PlanId == plan.Id);
            if (expired is not null) { break; }
            await Task.Delay(150, limit.Token);
        }

        Assert.Equal(ConfirmationOutcome.Expired, expired.Outcome);
        Assert.Equal(ConfirmationDenyReason.Expired, expired.Reason);

        var postRestartOutcome = await NeuronHosting.Get<IConfirmationRequests>(silo.Services, id).Outcome(plan.Id);
        Assert.NotNull(postRestartOutcome);
        Assert.Equal(ConfirmationOutcome.Expired, postRestartOutcome.Outcome);
    }
}
