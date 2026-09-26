using System.Net.Http.Json;
using DigitalBrain.Compute;
using IntoChat.Tests.E2E.Agent;

namespace IntoChat.Tests.E2E.Compute;

public sealed class ComputeLimitsFacts
{
    [Fact(Timeout = 240_000)]
    public async Task ComputePanelReadsTheSignedInAccountsLimits()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        await brain.Get<IAllowanceLedger>("owner")
            .SetLimitsAsync(new LimitPolicy { AccountLimitCompute = 100m }, ct);

        var report = await brain.HttpClient.GetFromJsonAsync<LimitReport>("/compute/limits", ct);

        Assert.NotNull(report);
        Assert.Equal("owner", report.AccountId);
        Assert.Equal(100m, report.LimitCompute);
    }
}
