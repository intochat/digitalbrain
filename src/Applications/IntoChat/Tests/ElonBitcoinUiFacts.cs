using System.Net.Http.Json;
using Microsoft.Playwright;

namespace IntoChat.Tests;

public sealed class ElonBitcoinUiFacts
{
    [Fact(Timeout = 240_000)]
    public async Task WebhookTweetAppearsInFlutterUi()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = await E2EDigitalBrain.StartAsync<Projects.IntoChat_AppHost>(cancellationToken: ct);
        using var intoChat = app.CreateHttpClient("IntoChat");
        using var webhook = await intoChat.PostAsJsonAsync(
            "/twitter/webhook",
            new { Account = "elonmusk", Text = "Bitcoin to the moon" },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, webhook.StatusCode);

        using var flutterHttp = app.CreateHttpClient("Flutter");
        var flutterUrl = flutterHttp.BaseAddress
            ?? throw new InvalidOperationException("Flutter HTTP endpoint is not allocated.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase),
        });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(flutterUrl.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByText("Bitcoin to the moon").WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 60_000,
        });
    }
}
