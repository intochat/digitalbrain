using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Playwright;

namespace DigitalBrain.AI.Web;

public sealed record WebPageObservation(string Url, string Title, string Text, IReadOnlyList<WebPageLink> Links);

public sealed record WebPageLink(string Text, string Url);

public sealed class PlaywrightBrowserSession : IAsyncDisposable
{
    private const int NavigationLimit = 12;
    private const int ResourceByteLimit = 4 * 1024 * 1024;
    private const int SessionByteLimit = 32 * 1024 * 1024;
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly IPage _page;
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _documentUrls = new(StringComparer.Ordinal);
    private readonly List<WebPageObservation> _observations = [];
    private readonly HashSet<string> _observedLinks = new(StringComparer.Ordinal);
    private readonly object _disposeLock = new();
    private CancellationTokenRegistration _cancellationRegistration;
    private Task? _disposeTask;
    private int _navigationCount;
    private int _resourceCount;
    private int _receivedBytes;

    private PlaywrightBrowserSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        _page = page;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = ConnectPublicAsync,
        }) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public IReadOnlyList<WebPageObservation> Observations => _observations.AsReadOnly();

    public static async Task<PlaywrightBrowserSession> CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        IBrowser? browser = null;
        try
        {
            PlaywrightException? launchError = null;
            foreach (var channel in new string?[] { null, "msedge", "chrome" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Channel = channel,
                        Headless = true,
                        ChromiumSandbox = true,
                        Timeout = 15_000,
                        // Network responses are supplied by the validated, pinned-DNS transport below.
                        // This also prevents browser network paths outside request interception.
                        Proxy = new Proxy { Server = "http://127.0.0.1:9", Bypass = "<-loopback>" },
                        Args = ["--disable-background-networking", "--force-webrtc-ip-handling-policy=disable_non_proxied_udp"],
                    }).WaitAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (PlaywrightException exception)
                {
                    launchError = exception;
                }
            }

            if (browser is null)
            {
                throw new InvalidOperationException(
                    "Web browsing requires a Playwright Chromium installation or an installed Microsoft Edge/Google Chrome browser.",
                    launchError);
            }

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                AcceptDownloads = false,
                ServiceWorkers = ServiceWorkerPolicy.Block,
                IgnoreHTTPSErrors = false,
                Permissions = [],
                ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            }).WaitAsync(cancellationToken).ConfigureAwait(false);
            var page = await context.NewPageAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            var session = new PlaywrightBrowserSession(playwright, browser, context, page);
            try
            {
                context.SetDefaultTimeout(15_000);
                context.SetDefaultNavigationTimeout(15_000);
                await context.RouteAsync("**/*", session.HandleRequestAsync).ConfigureAwait(false);
                await context.RouteWebSocketAsync("**/*", socket => _ = socket.CloseAsync()).ConfigureAwait(false);
                session._cancellationRegistration = cancellationToken.UnsafeRegister(
                    static state => ((PlaywrightBrowserSession)state!).RequestClose(), session);
                cancellationToken.ThrowIfCancellationRequested();
                return session;
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            if (browser is not null)
            {
                try { await browser.CloseAsync().ConfigureAwait(false); }
                catch (PlaywrightException) { }
            }

            playwright.Dispose();
            throw;
        }
    }

    public Task<WebPageObservation> NavigateAsync(string url, CancellationToken cancellationToken)
        => ObserveAsync(url, followObservedLink: false, cancellationToken);

    public Task<WebPageObservation> FollowLinkAsync(string url, CancellationToken cancellationToken)
        => ObserveAsync(url, followObservedLink: true, cancellationToken);

    public Task<WebPageObservation> SnapshotAsync(CancellationToken cancellationToken)
        => ObserveAsync(null, followObservedLink: false, cancellationToken);

    private async Task<WebPageObservation> ObserveAsync(string? url, bool followObservedLink, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _operation.WaitAsync(linked.Token).ConfigureAwait(false);
        using var cancellation = linked.Token.UnsafeRegister(static state => ((PlaywrightBrowserSession)state!).RequestClose(), this);
        try
        {
            if (url is not null)
            {
                var uri = PublicUri(url);
                if (followObservedLink && !_observedLinks.Contains(uri.AbsoluteUri))
                {
                    throw new ArgumentException("FollowLink accepts only HTTP(S) URLs present in an observed page's links.", nameof(url));
                }

                if (Volatile.Read(ref _navigationCount) >= NavigationLimit)
                {
                    throw new InvalidOperationException($"Web browsing is limited to {NavigationLimit} page navigations per session.");
                }

                await ResolvePublicAsync(uri.IdnHost, linked.Token).ConfigureAwait(false);
                await _page.GotoAsync(uri.AbsoluteUri, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 15_000,
                }).WaitAsync(linked.Token).ConfigureAwait(false);
            }

            var currentUrl = _documentUrls.GetValueOrDefault(_page.Url, _page.Url);
            PublicUri(currentUrl);
            var snapshot = await _page.EvaluateAsync<JsonElement>("""
                baseUrl => {
                    const body = (document.body?.innerText || '').replace(/\n{3,}/g, '\n\n').trim();
                    const text = body.length > 14000 ? body.slice(0, 10000) + '\n[Middle of page omitted]\n' + body.slice(-3900) : body;
                    const links = Array.from(document.querySelectorAll('a[href]')).map(a => {
                        try { return { text: (a.innerText || a.getAttribute('aria-label') || '').trim().slice(0, 160), url: new URL(a.getAttribute('href'), baseUrl).href }; }
                        catch { return null; }
                    }).filter(a => a && a.url.length <= 2048 && /^(https?:|mailto:)/i.test(a.url));
                    const unique = [...new Map(links.map(a => [a.url, a])).values()];
                    unique.sort((a, b) => Number(/mailto:|contact|about|impressum|legal|privacy/i.test(b.url + ' ' + b.text)) - Number(/mailto:|contact|about|impressum|legal|privacy/i.test(a.url + ' ' + a.text)));
                    return { title: document.title.slice(0, 300), text, links: unique.slice(0, 60) };
                }
                """, currentUrl).WaitAsync(linked.Token).ConfigureAwait(false);
            var links = snapshot.GetProperty("links").EnumerateArray()
                .Select(static item => new WebPageLink(item.GetProperty("text").GetString()!, item.GetProperty("url").GetString()!))
                .ToArray();
            foreach (var link in links)
            {
                _observedLinks.Add(link.Url);
            }

            var observation = new WebPageObservation(currentUrl, snapshot.GetProperty("title").GetString()!, snapshot.GetProperty("text").GetString()!, links);
            _observations.Add(observation);
            return observation;
        }
        catch (Exception) when (linked.IsCancellationRequested)
        {
            throw new OperationCanceledException("Web browsing was cancelled.", cancellationToken.IsCancellationRequested ? cancellationToken : linked.Token);
        }
        finally
        {
            _operation.Release();
        }
    }

    private async Task HandleRequestAsync(IRoute route)
    {
        try
        {
            if (route.Request.Method is not ("GET" or "HEAD")
                || route.Request.ResourceType is "image" or "media" or "font"
                || Interlocked.Increment(ref _resourceCount) > 400
                || route.Request.IsNavigationRequest && route.Request.Frame == _page.MainFrame
                    && Interlocked.Increment(ref _navigationCount) > NavigationLimit)
            {
                await route.AbortAsync("blockedbyclient").ConfigureAwait(false);
                return;
            }

            var uri = PublicUri(route.Request.Url);
            for (var redirect = 0; redirect <= 8; redirect++)
            {
                await ResolvePublicAsync(uri.IdnHost, _lifetime.Token).ConfigureAwait(false);
                using var request = new HttpRequestMessage(new HttpMethod(route.Request.Method), uri);
                foreach (var header in route.Request.Headers)
                {
                    if (header.Key is "accept" or "accept-language" or "user-agent")
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _lifetime.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    var location = response.Headers.Location ?? throw new InvalidOperationException("A web redirect did not contain a destination.");
                    uri = PublicUri(new Uri(uri, location).AbsoluteUri);
                    continue;
                }

                if (response.Content.Headers.ContentLength > ResourceByteLimit
                    || response.Content.Headers.ContentDisposition?.DispositionType.Equals("attachment", StringComparison.OrdinalIgnoreCase) == true)
                {
                    throw new InvalidOperationException("The page requested a download or an oversized resource.");
                }

                await using var source = await response.Content.ReadAsStreamAsync(_lifetime.Token).ConfigureAwait(false);
                using var body = new MemoryStream();
                var buffer = new byte[16 * 1024];
                int count;
                while ((count = await source.ReadAsync(buffer, _lifetime.Token).ConfigureAwait(false)) > 0)
                {
                    if (body.Length + count > ResourceByteLimit || Interlocked.Add(ref _receivedBytes, count) > SessionByteLimit)
                    {
                        throw new InvalidOperationException("Web browsing exceeded its response size limit.");
                    }

                    body.Write(buffer, 0, count);
                }

                var headers = response.Headers.Concat(response.Content.Headers)
                    .Where(static header => !header.Key.Equals("content-encoding", StringComparison.OrdinalIgnoreCase)
                        && !header.Key.Equals("content-length", StringComparison.OrdinalIgnoreCase)
                        && !header.Key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase)
                        && !header.Key.Equals("set-cookie", StringComparison.OrdinalIgnoreCase)
                        && !header.Key.Equals("refresh", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(static header => header.Key, static header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);
                if (route.Request.IsNavigationRequest && route.Request.Frame == _page.MainFrame)
                {
                    _documentUrls[route.Request.Url] = uri.AbsoluteUri;
                }

                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = (int)response.StatusCode,
                    Headers = headers,
                    BodyBytes = body.ToArray(),
                }).ConfigureAwait(false);
                return;
            }

            throw new InvalidOperationException("The page exceeded its redirect limit.");
        }
        catch (Exception exception) when (exception is HttpRequestException or SocketException or ArgumentException
            or InvalidOperationException or OperationCanceledException or PlaywrightException or IOException)
        {
            // A denied subresource must not crash the request event dispatcher.
            try { await route.AbortAsync("blockedbyclient").ConfigureAwait(false); }
            catch (PlaywrightException) { }
        }
    }

    private static Uri PublicUri(string url)
    {
        if (url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort || uri.IsLoopback || string.IsNullOrWhiteSpace(uri.IdnHost))
        {
            throw new ArgumentException("Web browsing accepts only public HTTP(S) URLs on their standard ports, without credentials.", nameof(url));
        }

        return uri;
    }

    private static async Task<IPAddress[]> ResolvePublicAsync(string host, CancellationToken cancellationToken)
    {
        var normalized = host.TrimEnd('.');
        if (normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || !normalized.Contains('.', StringComparison.Ordinal) && !IPAddress.TryParse(normalized, out _))
        {
            throw new ArgumentException("Local network names are unavailable to web browsing.", nameof(host));
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(static address => !IsPublic(address)))
        {
            throw new ArgumentException("Web browsing cannot access private, loopback, link-local, multicast, or reserved network addresses.", nameof(host));
        }

        return addresses;
    }

    private static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && (bytes[1] == 168 || bytes[1] == 0 && bytes[2] is 0 or 2
                    || bytes[1] == 88 && bytes[2] == 99))
                && !(bytes[0] == 198 && (bytes[1] is 18 or 19 || bytes[1] == 51 && bytes[2] == 100))
                && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }

        // Only globally routed IPv6 unicast; exclude transition/documentation allocations.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xe0) == 0x20
            && !(bytes[0] == 0x20 && bytes[1] == 0x02)
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && (bytes[2] < 2 || bytes[2] == 0x0d && bytes[3] == 0xb8))
            && !(bytes[0] == 0x3f && bytes[1] == 0xff && (bytes[2] & 0xf0) == 0);
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await ResolvePublicAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        SocketException? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                // Connect to the validated address itself, so DNS cannot change between validation and connection.
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                lastError = exception;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("The public website could not be reached.", lastError);
    }

    private void RequestClose() => _ = DisposeAsync().AsTask();

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            return new ValueTask(_disposeTask ??= Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try { await _context.CloseAsync().ConfigureAwait(false); }
        catch (PlaywrightException) { }
        try { await _browser.CloseAsync().ConfigureAwait(false); }
        catch (PlaywrightException) { }
        _httpClient.Dispose();
        _playwright.Dispose();
        _cancellationRegistration.Unregister();
        GC.SuppressFinalize(this);
    }
}
