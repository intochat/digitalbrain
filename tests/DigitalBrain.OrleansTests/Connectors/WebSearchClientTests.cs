using System.Net;
using System.Text;
using DigitalBrain.Integrations.Web;

namespace DigitalBrain.Tests.Integrations;

public sealed class WebSearchClientTests
{
    [Fact]
    public async Task SearchAsync_sends_the_bounded_query_and_projects_safe_results()
    {
        var handler = new RecordingHandler("""
            {"web":{"results":[{"title":"Northstar Robotics","url":"https://northstar.example","description":"Warehouse robotics company."}]}}
            """);
        var client = new BraveWebSearchClient(new HttpClient(handler), "secret-key");

        var response = await client.SearchAsync(new("Northstar Robotics", 3));

        var result = Assert.Single(response.Results);
        Assert.Equal("Northstar Robotics", result.Title);
        Assert.Equal("https://northstar.example/", result.Url);
        Assert.Equal("Warehouse robotics company.", result.Snippet);
        Assert.Equal("secret-key", handler.ApiKey);
        Assert.Contains("q=Northstar%20Robotics", handler.RequestUri?.Query, StringComparison.Ordinal);
        Assert.Contains("count=3", handler.RequestUri?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_rejects_non_https_results()
    {
        var handler = new RecordingHandler("""
            {"web":{"results":[{"title":"Unsafe","url":"http://example.test","description":"Unsafe result."}]}}
            """);
        var client = new BraveWebSearchClient(new HttpClient(handler), "secret-key");

        var response = await client.SearchAsync(new("unsafe", 1));

        Assert.Empty(response.Results);
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public string? ApiKey { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ApiKey = request.Headers.GetValues("X-Subscription-Token").Single();
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
