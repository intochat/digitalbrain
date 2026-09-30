using DigitalBrain.CustomerResearcher;
using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.Playwright;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Modules.CustomerResearcher.Tests.Unit;

public sealed class ResearchWorkflowFacts
{
    [Fact]
    public async Task BrowsesBeforeModelAndExtractsOnlyAfterOpeningSource()
    {
        var browser = new Browser();
        var calls = 0;
        using var client = new Client((messages, options, ct) =>
        {
            Assert.NotEmpty(browser.Urls);
            calls++;
            if (calls == 1)
            {
                Assert.NotNull(options?.ResponseFormat);
                Assert.Contains(messages, message => message.Text.Contains("https://acme.example/", StringComparison.Ordinal));
                return Task.FromResult("""{"linkId":1}""");
            }
            Assert.NotNull(options?.ResponseFormat);
            Assert.Contains(messages, message => message.Text.Contains("Acme builds widgets", StringComparison.Ordinal));
            return Task.FromResult("""{"status":"found","companyName":"Acme","website":"https://acme.example/"}""");
        });
        var result = await new CompanyResearchAgent(client).Research("Acme", browser, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Company);
        Assert.Equal(2, calls);
        Assert.Contains("bing.com/search", browser.Urls[0]);
    }

    [Fact]
    public async Task ModelThatSkipsToolsCannotReportResearchCompleted()
    {
        var browser = new Browser();
        using var client = new Client((_, _, _) => Task.FromResult("""{"status":"found","website":"https://acme.example/"}"""));
        var result = await new CompanyResearchAgent(client).Research("Acme", browser, TestContext.Current.CancellationToken);
        Assert.NotEmpty(browser.Urls);
        Assert.Null(result.Company);
        Assert.Contains("select", result.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyNavigationReportsBrowserFailureWithoutCallingModel()
    {
        var browser = new Browser { Empty = true };
        using var client = new Client((_, _, _) => throw new InvalidOperationException("Model must not run without a readable page"));
        var result = await new CompanyResearchAgent(client).Research("Acme", browser, TestContext.Current.CancellationToken);
        Assert.Null(result.Company);
        Assert.Contains("readable", result.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContactEvidenceSurvivesLaterAboutPageAndRefreshFailure()
    {
        var browser = new Browser
        {
            FailSnapshot = true,
            Page = url => url.Contains("bing.com", StringComparison.Ordinal)
                ? new(url, "Search", "Acme official website", [new("Acme", "https://www.bing.com/ck/a?u=a1aHR0cHM6Ly9hY21lLmV4YW1wbGUv")])
                : url.EndsWith("/contact", StringComparison.Ordinal)
                    ? new(url, "Contact", new string('x', 3000) + " Acme headquarters: Amsterdam. sales@acme.example", [new("About", "https://acme.example/about")])
                    : url.EndsWith("/about", StringComparison.Ordinal)
                        ? new(url, "About", "Acme builds widgets", [])
                        : new(url, "Acme", "Acme builds widgets", [new("Contact", "https://acme.example/contact")]),
        };
        var calls = 0;
        using var client = new Client((messages, options, _) =>
        {
            Assert.True(string.Join("", messages.Select(message => message.Text)).Length < 10000);
            if (++calls <= 3) { return Task.FromResult("""{"linkId":1}"""); }
            Assert.Contains(messages, message => message.Text.Contains("sales@acme.example", StringComparison.Ordinal));
            return Task.FromResult("""{"status":"found","companyName":"Acme","website":"https://acme.example/","email":"sales@acme.example"}""");
        });
        var result = await new CompanyResearchAgent(client).Research("Acme", browser, TestContext.Current.CancellationToken);
        Assert.Equal("sales@acme.example", result.Company?.Email);
        Assert.Contains("https://acme.example/", browser.Urls);
        Assert.Equal(4, browser.Urls.Count);
    }
    private sealed class Client(Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<string>> respond) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            new(new ChatMessage(ChatRole.Assistant, await respond(messages.ToArray(), options, cancellationToken)));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class Browser : IPlaywright
    {
        public readonly List<string> Urls = [];
        public bool Empty;
        public bool FailSnapshot;
        public Func<string, BrowserObservation>? Page;
        public Task<BrowserObservation> Navigate(string url, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Urls.Add(url);
            return Task.FromResult(Page?.Invoke(url) ?? new BrowserObservation(url, "Acme", Empty ? "" : "Acme builds widgets", [new("Acme", "https://acme.example/")]));
        }
        public Task<BrowserObservation> Snapshot(CancellationToken ct = default) => FailSnapshot ? throw new InvalidOperationException("Refresh failed") : Navigate(Urls.Last(), ct);
        public Task<BrowserObservation> Click(string selector, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BrowserObservation> Fill(string selector, string value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Attach(BrowserAttachment attachment, CancellationToken ct = default) => Task.CompletedTask;
        public Task Detach(string sessionId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<BrowserSession> Read() => Task.FromResult(new BrowserSession(true, "test", Urls.LastOrDefault(), "Acme"));
        public Task<Guid> Watch(INeuronObserver observer) => throw new NotSupportedException();
        public Task Unwatch(INeuronObserver observer) => Task.CompletedTask;
    }
}
