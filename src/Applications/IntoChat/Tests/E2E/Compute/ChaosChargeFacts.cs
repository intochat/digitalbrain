using System.Net.Http.Json;
using DigitalBrain.AI;
using DigitalBrain.Compute;
using DigitalBrain.Contracts.Enforcement;
using IntoChat.Tests.E2E.Agent;

namespace IntoChat.Tests.E2E.Compute;

// Chaos and prompt-injection facts for the allowance path. The model never reaches a paid call
// without an allowance: a scripted sequence of tool calls is replayed as CallRequests against the
// one durable ledger and every call without an allowance is stopped.
public sealed class ChaosChargeFacts
{
    [Fact(Timeout = 240_000)]
    public async Task ChaosFindsNoDoubleChargesAndActualNeverExceedsTheLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
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
        Assert.True(report.SpentCompute <= report.LimitCompute);
    }

    [Fact(Timeout = 240_000)]
    public async Task AScriptedModelMakesZeroPaidCallsWithoutAnAllowance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        var ledger = brain.Get<IAllowanceLedger>("account-injected");

        var injected = new[]
        {
            Paid("account-injected", "ws-injected", 40m, "inject-1", operation: "RemoveBackground"),
            Paid("account-injected", "ws-injected", 40m, "inject-2", operation: "EnrichCompany"),
            Paid("account-injected", "ws-injected", 500m, "inject-3", operation: "RunAgent"),
        };

        foreach (var request in injected)
        {
            var denied = await ledger.AuthorizeAsync(request, ct);
            Assert.False(denied.Allowed);
            Assert.Equal(CallDenial.MissingAllowance, denied.Denial);
        }

        Assert.Empty(await ledger.ReadReservationsAsync(ct));
        Assert.Equal(3, (await ledger.ListPendingAsync(ct)).Length);
    }

    [Fact(Timeout = 240_000)]
    public async Task ARevokedGrantAndAPermissionGrowthRequireReconsent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        var ledger = brain.Get<IAllowanceLedger>("account-consent");

        await ledger.GrantAsync(new Allowance
        {
            AllowanceId = "consent-app",
            AccountId = "account-consent",
            WorkspaceId = "ws-consent",
            AppId = "app-1",
            Level = ApprovalLevel.InstallConsent,
            Scope = AllowanceScope.Always,
            LimitCompute = 20m,
            MonthlyLimitCompute = 20m,
            PriceBookVersion = new PriceBook().Version,
            GrantedAt = DateTimeOffset.UtcNow,
        }, ct);

        var allowed = await ledger.AuthorizeAsync(Paid("account-consent", "ws-consent", 5m, "i-1", appId: "app-1"), ct);
        Assert.True(allowed.Allowed);

        var grown = await ledger.AuthorizeAsync(
            Paid("account-consent", "ws-consent", 5m, "i-2", "app-1", "Render", "person.birthDate"), ct);
        Assert.False(grown.Allowed);
        Assert.Contains("re-consent", grown.Explanation!, StringComparison.Ordinal);
    }

    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==";

    // The paid app operation is reachable through a real scripted-model tool call, and without an
    // allowance the one call filter stops it: the ledger holds no reservation and nothing is charged.
    [Fact(Timeout = 300_000)]
    public async Task AScriptedModelToolCallChargesNothingWithoutAnAllowance()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("intochat-chaos-paid-").FullName;
        try
        {
            foreach (var name in new[] { "photo-1.png", "photo-2.png", "photo-3.png" })
            {
                await File.WriteAllBytesAsync(Path.Combine(root, name), Convert.FromBase64String(OnePixelPng), ct);
            }

            await using var model = await ScriptedModelServer.StartAsync(ct);
            await using var brain = await IntoChatE2ETest.Create(privateConfiguration: new()
            {
                ["IntoChat:LocalFiles:Roots:downloads"] = root,
                ["IntoChat:LocalFiles:AssetDirectory"] = Path.Combine(root, "assets"),
            })
                .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
                .StartAsync(ct);

            const string workspace = "chaos-paid";
            async Task<string> Ask(string run, string message)
            {
                using var response = await brain.HttpClient.PostAsJsonAsync("/agent",
                    new { workspaceId = workspace, threadId = "thread", runId = run, messages = new[] { new { role = "user", content = message } } }, ct);
                return await response.Content.ReadAsStringAsync(ct);
            }

            var planned = await Ask("plan", "Remove the background from these 3 product photos");
            Assert.Contains("UI_CARD", planned, StringComparison.Ordinal);
            Assert.Contains("\"planId\"", planned, StringComparison.Ordinal);

            var denied = await Ask("run", "Run it without approval");
            Assert.Contains("\"requiresApproval\":true", denied, StringComparison.Ordinal);
            Assert.Contains("\"chargedCompute\":0", denied, StringComparison.Ordinal);

            var ledger = brain.Get<IAllowanceLedger>("owner");
            Assert.Empty(await ledger.ReadReservationsAsync(ct));
            Assert.Equal(0m, (await ledger.ReadLimitsAsync(ct)).SpentCompute);
        }
        finally { Directory.Delete(root, true); }
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
                WorkspaceId = workspace,
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
