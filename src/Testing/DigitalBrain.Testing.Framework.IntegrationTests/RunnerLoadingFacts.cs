using System.Net;
using System.Net.Http.Json;
using System.Text;
using DigitalBrain.Contracts;
using DigitalBrain.Google;
using Xunit;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;

namespace DigitalBrain.Tests;

public sealed class RunnerLoadingFacts
{
    [Fact(Timeout = 180_000)]
    public async Task GoogleLoadsWithoutAStaticRunnerReference()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var run = await IntegrationTest.StartAsync(new() { Modules = [GoogleModule.Define(new())] }, ct);
        Assert.NotEqual(Environment.ProcessId, await run.HttpClient.GetFromJsonAsync<int>("/process", ct));
        var gmail = run.Get<IGmail>("runner-probe");
        await using var received = await run.Observe<MailReceived>(gmail, ct);
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "{\"emailAddress\":\"runner-probe\",\"historyId\":\"123\"}"));
        using var response = await run.HttpClient.PostAsJsonAsync(
            "/google/gmail/watch", new { message = new { data } }, ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("123", (await received.NextAsync(ct: ct)).HistoryId);
    }
}

