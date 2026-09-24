using DigitalBrain.Compute;
using DigitalBrain.Compute.Allowances;
using DigitalBrain.Compute.Billing;
using DigitalBrain.Compute.Metering;
using DigitalBrain.Contracts.Enforcement;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests;

public sealed class AllowanceFacts
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly string PriceVersion = new PriceBook().Version;

    [Fact]
    public void APaidCallWithoutAnAllowanceIsDeniedAndPending()
    {
        var request = Paid("account", "ws-1", "chat-1", 5m);
        var outcome = AllowanceRules.Evaluate(request, Snapshot(), Now);

        Assert.False(outcome.Decision.Allowed);
        Assert.Equal(CallDenial.MissingAllowance, outcome.Decision.Denial);
        Assert.Equal(AllowanceRules.ApprovalId(request), outcome.Decision.ApprovalId);
    }

    [Fact]
    public void AStandingBudgetAllowsUpToItsLimitThenHardStops()
    {
        var budget = AllowanceOf("budget", "ws-1", ApprovalLevel.StandingBudget, AllowanceScope.Always, 10m);
        var within = AllowanceRules.Evaluate(Paid("account", "ws-1", "chat-1", 6m), Snapshot([budget]), Now);
        Assert.True(within.Decision.Allowed);

        var past = AllowanceRules.Evaluate(Paid("account", "ws-1", "chat-1", 11m), Snapshot([budget]), Now);
        Assert.False(past.Decision.Allowed);
        Assert.Equal(CallDenial.LimitReached, past.Decision.Denial);
    }

    [Fact]
    public void APerOperationAllowanceBeatsTheStandingBudget()
    {
        var budget = AllowanceOf("budget", "ws-1", ApprovalLevel.StandingBudget, AllowanceScope.Always, 1m);
        var operation = AllowanceOf("operation", "ws-1", ApprovalLevel.PerOperation, AllowanceScope.Once, 5m, appId: "app-1", operation: "Render");
        var request = Paid("account", "ws-1", "chat-1", 4m, appId: "app-1", operation: "Render");

        var outcome = AllowanceRules.Evaluate(request, Snapshot([budget, operation]), Now);

        Assert.True(outcome.Decision.Allowed);
        Assert.Equal("operation", outcome.Decision.AllowanceId);
    }

    [Fact]
    public void AOnceAllowanceIsConsumedByItsReservation()
    {
        var once = AllowanceOf("once", "ws-1", ApprovalLevel.PerOperation, AllowanceScope.Once, 5m, operation: "Render");
        var used = new AllowanceReservation
        {
            ReservationId = "res-1",
            AllowanceId = "once",
            ReservedCompute = 5m,
            OccurredAt = Now,
            WorkspaceId = "ws-1",
            Operation = "Render",
        };

        var outcome = AllowanceRules.Evaluate(Paid("account", "ws-1", "chat-1", 1m, operation: "Render"), Snapshot([once], [used]), Now);

        Assert.False(outcome.Decision.Allowed);
        Assert.Equal(CallDenial.MissingAllowance, outcome.Decision.Denial);
    }

    [Fact]
    public void AThisChatAllowanceNeedsTheMatchingConversation()
    {
        var scoped = AllowanceOf("scoped", "ws-1", ApprovalLevel.PerOperation, AllowanceScope.ThisChat, 5m,
            operation: "Render") with { ConversationId = "chat-1" };

        Assert.True(AllowanceRules.Evaluate(Paid("account", "ws-1", "chat-1", 1m, operation: "Render"), Snapshot([scoped]), Now).Decision.Allowed);
        Assert.Equal(CallDenial.MissingAllowance,
            AllowanceRules.Evaluate(Paid("account", "ws-1", "chat-2", 1m, operation: "Render"), Snapshot([scoped]), Now).Decision.Denial);
    }

    [Fact]
    public void InstallConsentMonthlyLimitResetsNextMonth()
    {
        var consent = AllowanceOf("consent", "ws-1", ApprovalLevel.InstallConsent, AllowanceScope.Always, 0m,
            appId: "app-1") with { MonthlyLimitCompute = 10m };
        var lastMonth = new AllowanceReservation
        {
            ReservationId = "res-old",
            AllowanceId = "consent",
            ReservedCompute = 10m,
            Settled = true,
            SettledCompute = 10m,
            OccurredAt = new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero),
            WorkspaceId = "ws-1",
            AppId = "app-1",
            Operation = "Render",
        };

        var nextMonth = AllowanceRules.Evaluate(Paid("account", "ws-1", "chat-1", 3m, appId: "app-1"), Snapshot([consent], [lastMonth]), Now);

        Assert.True(nextMonth.Decision.Allowed);
    }

    [Fact]
    public void PermissionOrPriceGrowthForcesReconsent()
    {
        var grown = Paid("account", "ws-1", "chat-1", 1m, appId: "app-1", permissions: "person.birthDate");
        var allowance = AllowanceOf("consent", "ws-1", ApprovalLevel.InstallConsent, AllowanceScope.Always, 5m, appId: "app-1");
        Assert.True(AllowanceRules.Evaluate(grown, Snapshot([allowance]), Now).Decision.Explanation!.Contains("re-consent", StringComparison.Ordinal));

        var stale = AllowanceOf("stale", "ws-1", ApprovalLevel.StandingBudget, AllowanceScope.Always, 5m) with { PriceBookVersion = "v-past" };
        var staleOutcome = AllowanceRules.Evaluate(Paid("account", "ws-1", "chat-1", 1m), Snapshot([stale]), Now);
        Assert.False(staleOutcome.Decision.Allowed);
        Assert.Contains("re-consent", staleOutcome.Decision.Explanation!, StringComparison.Ordinal);
    }

    [Fact]
    public void AlertsFireAtSeventyFiveNinetyAndOneHundredPercent()
    {
        var budget = AllowanceOf("budget", "ws-1", ApprovalLevel.StandingBudget, AllowanceScope.Always, 100m);
        var policy = new LimitPolicy { AccountLimitCompute = 100m };

        Assert.Equal(LimitAlert.At75, AllowanceRules.Evaluate(Paid("account", "ws-1", "chat-1", 80m), Snapshot([budget], policy: policy), Now).Alert);
        Assert.Equal(LimitAlert.At90, AllowanceRules.Evaluate(Paid("account", "ws-1", "chat-1", 95m), Snapshot([budget], policy: policy), Now).Alert);
        Assert.Equal(LimitAlert.At100, AllowanceRules.Evaluate(Paid("account", "ws-1", "chat-1", 100m), Snapshot([budget], policy: policy), Now).Alert);
    }

    [Fact]
    public void ReadAndExportSurviveTheFirstThirtyDaysOfAHardStop()
    {
        var snapshot = Snapshot(hardStoppedAt: Now);
        var within = AllowanceRules.Evaluate(ReadOnly("account", "ws-1"), snapshot, Now.AddDays(29));
        Assert.True(within.Decision.Allowed);

        var after = AllowanceRules.Evaluate(ReadOnly("account", "ws-1"), snapshot, Now.AddDays(31));
        Assert.Equal(CallDenial.LimitReached, after.Decision.Denial);
    }

    [Fact]
    public void OnlyPlatformProviderAndThirdPartyFaultsAreNeverCharged()
    {
        Assert.False(AllowanceRules.ShouldCharge(FailureClass.PlatformFault));
        Assert.False(AllowanceRules.ShouldCharge(FailureClass.ProviderOutage));
        Assert.False(AllowanceRules.ShouldCharge(FailureClass.ThirdPartyFault));
        Assert.True(AllowanceRules.ShouldCharge(FailureClass.ModelRetry));
        Assert.True(AllowanceRules.ShouldCharge(FailureClass.Cancelled));
        Assert.True(AllowanceRules.ShouldCharge(FailureClass.RevokedGrant));
        Assert.True(AllowanceRules.ShouldCharge(FailureClass.LimitReached));
    }

    [Fact]
    public void SettlementIsCappedAtTheRemainingLimit()
    {
        Assert.Equal(6m, AllowanceRules.CapSettlement(50m, 4m, 10m));
        Assert.Equal(50m, AllowanceRules.CapSettlement(50m, 0m, 0m));
        Assert.Equal(0m, AllowanceRules.CapSettlement(5m, 10m, 10m));
    }

    [Fact]
    public async Task TheStageAbstainsOnAnAllowedCallAndDeniesWithoutAnAllowance()
    {
        var ct = TestContext.Current.CancellationToken;
        var allowed = new AllowanceCallFilterStage(new FixedSource(AllowanceDecision.Allow()));
        Assert.Null(await allowed.EvaluateAsync(Paid("account", "ws-1", "chat-1", 1m), ct));

        var denied = new AllowanceCallFilterStage(new FixedSource(new AllowanceDecision
        {
            Allowed = false,
            Denial = CallDenial.MissingAllowance,
            Explanation = "No allowance.",
        }));
        var decision = await denied.EvaluateAsync(Paid("account", "ws-1", "chat-1", 1m), ct);
        Assert.Equal(CallDenial.MissingAllowance, decision!.Denial);
    }

    [Fact]
    public async Task PendingApprovalsSurviveARestart()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        var ledger = brain.Get<IAllowanceLedger>("account");

        var decision = await ledger.AuthorizeAsync(Paid("account", "ws-1", "chat-1", 5m), ct);
        Assert.Equal(CallDenial.MissingAllowance, decision.Denial);

        await brain.DeactivateAsync(ledger, ct);

        Assert.Single(await ledger.ListPendingAsync(ct));
    }

    [Fact]
    public async Task ConcurrentRetriesReserveAChargeOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var recording = new RecordingAlertSink();
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IComputeAlertSink>(recording))
            .StartAsync(ct);
        var ledger = brain.Get<IAllowanceLedger>("account");
        await ledger.GrantAsync(AllowanceOf("budget", "ws-1", ApprovalLevel.StandingBudget, AllowanceScope.Always, 100m), ct);
        var request = Paid("account", "ws-1", "chat-1", 5m, intent: "intent-chaos", operation: "Render");

        var decisions = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => ledger.AuthorizeAsync(request, ct)));

        Assert.All(decisions, decision => Assert.True(decision.Allowed));
        Assert.Single(await ledger.ReadReservationsAsync(ct));
    }

    [Fact]
    public async Task AlertsPostToTheInboxSinkAtEachThreshold()
    {
        var ct = TestContext.Current.CancellationToken;
        var recording = new RecordingAlertSink();
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IComputeAlertSink>(recording))
            .StartAsync(ct);
        var ledger = brain.Get<IAllowanceLedger>("account");
        await ledger.SetLimitsAsync(new LimitPolicy { AccountLimitCompute = 100m }, ct);
        await ledger.GrantAsync(AllowanceOf("budget", "ws-1", ApprovalLevel.StandingBudget, AllowanceScope.Always, 100m), ct);

        await ledger.AuthorizeAsync(Paid("account", "ws-1", "chat-1", 80m, intent: "i-1"), ct);
        await ledger.AuthorizeAsync(Paid("account", "ws-1", "chat-1", 15m, intent: "i-2"), ct);
        await ledger.AuthorizeAsync(Paid("account", "ws-1", "chat-1", 5m, intent: "i-3"), ct);

        Assert.Contains(recording.Alerts, alert => alert.Level == LimitAlert.At75);
        Assert.Contains(recording.Alerts, alert => alert.Level == LimitAlert.At90);
        Assert.Contains(recording.Alerts, alert => alert.Level == LimitAlert.At100);
    }

    [Fact]
    public async Task SettledActualNeverExceedsTheLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        var ledger = brain.Get<IAllowanceLedger>("account");
        await ledger.SetLimitsAsync(new LimitPolicy { AccountLimitCompute = 10m }, ct);
        await ledger.GrantAsync(AllowanceOf("budget", "ws-1", ApprovalLevel.StandingBudget, AllowanceScope.Always, 10m), ct);

        var decision = await ledger.AuthorizeAsync(Paid("account", "ws-1", "chat-1", 8m, intent: "i-1"), ct);
        var settled = await ledger.SettleAsync(decision.ReservationId!, 50m, FailureClass.ModelRetry, ct);

        Assert.Equal(10m, settled.SettledCompute);
        var report = await ledger.ReadLimitsAsync(ct);
        Assert.True(report.SpentCompute <= report.LimitCompute);
        Assert.True(report.HardStopped);
    }

    [Fact]
    public async Task PlatformFaultsAndProviderOutagesAreNotCharged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        var ledger = brain.Get<IAllowanceLedger>("account");
        await ledger.GrantAsync(AllowanceOf("budget", "ws-1", ApprovalLevel.StandingBudget, AllowanceScope.Always, 10m), ct);

        var fault = await ledger.AuthorizeAsync(Paid("account", "ws-1", "chat-1", 4m, intent: "i-1"), ct);
        await ledger.SettleAsync(fault.ReservationId!, 4m, FailureClass.ProviderOutage, ct);
        var outage = await ledger.AuthorizeAsync(Paid("account", "ws-1", "chat-1", 4m, intent: "i-2"), ct);
        await ledger.SettleAsync(outage.ReservationId!, 4m, FailureClass.PlatformFault, ct);

        var report = await ledger.ReadLimitsAsync(ct);
        Assert.Equal(0m, report.SpentCompute);
    }

    [Fact]
    public void RecurringMetersAccrueWithoutAUserIntentAndAppearOnTheStatement()
    {
        var meter = RecurringMeters.Accrue("ws-1", "storage.gb_month", 2m, "gb_month", new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal("recurring:storage.gb_month:2026-09", meter.IntentId);

        var statement = StatementBuilder.Build("account", "2026-09", [], [meter], new PriceBook(), Now);

        var line = Assert.Single(statement.Lines);
        Assert.True(line.Recurring);
        Assert.Equal("storage.gb_month", line.MeterId);
    }

    [Fact]
    public async Task TheFakeInvoicingProviderIsDeterministicAndNeverCallsOut()
    {
        var invoice = new Invoice
        {
            InvoiceId = "inv-1",
            AccountId = "account",
            Period = "2026-09",
            AmountUsd = 12.5m,
            Currency = "usd",
        };

        var provider = new FakeInvoicingProvider();
        var first = await provider.CreateAsync(invoice, TestContext.Current.CancellationToken);
        var second = await provider.CreateAsync(invoice, TestContext.Current.CancellationToken);

        Assert.Equal(InvoiceStatus.Open, first.Status);
        Assert.Equal(first.ProviderReference, second.ProviderReference);
        Assert.Equal("fake-invoice:inv-1", first.ProviderReference);
    }

    [Fact]
    public void AStatementMapsToADeterministicInvoice()
    {
        var meter = RecurringMeters.Accrue("ws-1", "storage.gb_month", 2m, "gb_month", new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));
        var statement = StatementBuilder.Build("account", "2026-09", [], [meter], new PriceBook(), Now);

        var invoice = BillingService.InvoiceFor(statement);

        Assert.Equal("inv:account:2026-09", invoice.InvoiceId);
        Assert.Equal(statement.Lines, invoice.Lines);
        Assert.Equal(statement.TotalUsd, invoice.AmountUsd);
    }

    private static Allowance AllowanceOf(string id, string workspace, ApprovalLevel level, AllowanceScope scope, decimal limit,
        string? appId = null, string? operation = null)
        => new()
        {
            AllowanceId = id,
            AccountId = "account",
            WorkspaceId = workspace,
            AppId = appId,
            Operation = operation,
            Level = level,
            Scope = scope,
            LimitCompute = limit,
            PriceBookVersion = PriceVersion,
            GrantedAt = Now,
        };

    private static AllowanceSnapshot Snapshot(
        IReadOnlyList<Allowance>? allowances = null,
        IReadOnlyList<AllowanceReservation>? reservations = null,
        LimitPolicy? policy = null,
        DateTimeOffset? hardStoppedAt = null)
        => new(allowances ?? [], reservations ?? [], policy ?? new LimitPolicy(), PriceVersion, LimitAlert.None, hardStoppedAt);

    private static CallRequest Paid(string account, string workspace, string? conversation, decimal compute,
        string? appId = null, string operation = "Render", string? intent = null, params string[] permissions)
        => new()
        {
            Caller = new CallerContext
            {
                PrincipalId = "principal",
                AccountId = account,
                WorkspaceId = workspace,
                Kind = CallerKind.Assistant,
                StampedBy = TrustedEdge.AuthenticatedHttp,
                AppId = appId,
                ConversationId = conversation,
                IntentId = intent ?? $"intent-{Guid.NewGuid():N}",
            },
            TargetNeuron = "neuron",
            Operation = operation,
            EstimatedCompute = compute,
            HasSideEffects = true,
            SemanticTypeIds = permissions,
        };

    private static CallRequest ReadOnly(string account, string workspace) => new()
    {
        Caller = new CallerContext
        {
            PrincipalId = "principal",
            AccountId = account,
            WorkspaceId = workspace,
            Kind = CallerKind.Assistant,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        },
        TargetNeuron = "neuron",
        Operation = "Read",
    };

    private sealed class FixedSource(AllowanceDecision decision) : IAllowancePolicySource
    {
        public ValueTask<AllowanceDecision> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult(decision);
    }

    private sealed class RecordingAlertSink : IComputeAlertSink
    {
        public List<ComputeLimitAlert> Alerts { get; } = [];

        public Task RaiseAsync(ComputeLimitAlert alert, CancellationToken cancellationToken = default)
        {
            Alerts.Add(alert);
            return Task.CompletedTask;
        }
    }
}
