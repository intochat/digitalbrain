using Brain.Contracts;
using Brain.Modules.Sdk;
using Xunit;

namespace Brain.KernelTests;

public class WebKindTests(BrainClusterFixture<WebKindsConfigurator> fixture)
    : BrainTest<WebKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Successful_fetch_journals_status_and_body()
    {
        WebKindsConfigurator.Handler.Reset();
        WebKindsConfigurator.Handler.Body = "hello web";

        var web = Neuron("web", "success");
        var receipt = await web.InvokeAsync(new("web.fetch.v1", """{"url":"https://example.com/"}""", "cmd-1", OwnerSession));
        Assert.Contains("hello web", receipt.OutputJson);
        Assert.Contains("\"status\":200", receipt.OutputJson);

        var events = await web.ReadEventsAsync(0, 10);
        Assert.Single(events.Events);
        Assert.Equal("web.fetched", events.Events[0].Kind);
        Assert.Contains("\"status\":200", events.Events[0].PayloadJson);
    }

    [Fact]
    public async Task Non_http_scheme_fails_closed()
    {
        var web = Neuron("web", "guard");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            web.InvokeAsync(new("web.fetch.v1", """{"url":"ftp://example.com/file"}""", "cmd-1", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Equal(0, (await web.ReadAsync("recent")).Revision);
    }

    [Fact]
    public async Task Handler_failure_maps_to_provider_error()
    {
        WebKindsConfigurator.Handler.Reset();
        WebKindsConfigurator.Handler.Throws = new HttpRequestException("boom");

        var web = Neuron("web", "error");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            web.InvokeAsync(new("web.fetch.v1", """{"url":"https://example.com/"}""", "cmd-1", OwnerSession)));
        Assert.Equal(BrainErrors.ProviderError, exception.Code);
        Assert.Equal(0, (await web.ReadAsync("recent")).Revision);

        WebKindsConfigurator.Handler.Reset();
    }

    [Fact]
    public async Task Duplicate_command_id_does_not_refetch()
    {
        WebKindsConfigurator.Handler.Reset();
        WebKindsConfigurator.Handler.Body = "dup body";

        var web = Neuron("web", "dup");
        var first = await web.InvokeAsync(new("web.fetch.v1", """{"url":"https://example.com/"}""", "cmd-dup", OwnerSession));
        var callsAfterFirst = WebKindsConfigurator.Handler.Calls;
        var second = await web.InvokeAsync(new("web.fetch.v1", """{"url":"https://example.com/"}""", "cmd-dup", OwnerSession));
        Assert.Equal(first, second);
        Assert.Equal(callsAfterFirst, WebKindsConfigurator.Handler.Calls);
    }
}
