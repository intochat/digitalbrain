using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IntoChat.Tests;

public sealed class HealthFacts
{
    [Fact]
    public async Task IntoChatHealthReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var timeout = TimeSpan.FromMinutes(3);
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IntoChat_AppHost>(
            ["Testing:SkipFlutterHost=true"], ct).WaitAsync(timeout, ct);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Aspire.", LogLevel.Information);
        });
        await using var app = await appHost.BuildAsync(ct).WaitAsync(timeout, ct);
        await app.StartAsync(ct).WaitAsync(timeout, ct);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("IntoChat", ct).WaitAsync(timeout, ct);
        using var http = app.CreateHttpClient("IntoChat");
        using var response = await http.GetAsync("/health", ct).WaitAsync(timeout, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
