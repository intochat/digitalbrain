using DigitalBrain.Testing.Unit;
using DigitalBrain.Compute;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Compute.Allowances;

namespace DigitalBrain.Modules.Compute.Tests.Unit;

// Chaos and prompt-injection facts for the allowance path. The model never reaches a paid call
// without an allowance: a scripted sequence of tool calls is replayed as CallRequests against the
// one durable ledger and every call without an allowance is stopped.
public sealed class ChaosChargeFacts
{
    [Fact]
    public async Task ChaosFindsNoDoubleChargesAndActualNeverExceedsTheLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        var ledger = brain.Get<IAllowanceLedger>("account-chaos");
        await ledger.SetLimitsAsync(new LimitPolicy { AccountLimitCompute = 100m }, ct);
        await ledger.GrantAsync(Allowance("account-chaos", "ws-chaos", 100m), ct);
        var request = Paid("account-chaos", "ws-chaos", 12m, "intent-chaos");

        var decisions = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => ledger.AuthorizeAsync(request, ct)));

        Assert.All(decisions, decision => Assert.True(decision.Allowed));
        Assert.Single(await ledger.ReadReservationsAsync(ct));

        var reservation = (await ledger.ReadReservationsAsync(ct))[0];
        await ledger.SettleAsync(reservation.ReservationId, 90m, FailureClass.ModelRetry, ct);

        var report = await ledger.ReadLimitsAsync(ct);
        Assert.Equal(90m, report.SpentCompute);
        Assert.True(report.SpentCompute <= report.LimitCompute);
    }

    private static Allowance Allowance(string account, string workspace, decimal limit) => new()
    {
        AllowanceId = "budget",
        AccountId = account,
        WorkspaceId = workspace,
        Level = ApprovalLevel.StandingBudget,
        Scope = AllowanceScope.Always,
        LimitCompute = limit,
        PriceBookVersion = new PriceBook().Version,
        GrantedAt = DateTimeOffset.UtcNow,
    };

    private static CallRequest Paid(string account, string workspace, decimal compute, string intent,
        string? appId = null, string operation = "Render", params string[] permissions) => new()
        {
            Caller = new CallerContext
            {
                PrincipalId = "principal",
                AccountId = account,
                BrainId = workspace,
                Kind = CallerKind.Assistant,
                StampedBy = TrustedEdge.AuthenticatedHttp,
                AppId = appId,
                ConversationId = "chat-1",
                IntentId = intent,
            },
            TargetNeuron = "neuron",
            Operation = operation,
            EstimatedCompute = compute,
            HasSideEffects = true,
            SemanticTypeIds = permissions,
        };
}

