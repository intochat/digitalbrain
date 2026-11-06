using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;
using DigitalBrain.Runtime.Http.Storage;
using DigitalBrain.Spi;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Http.Tests.Integration;

public sealed class StorageContentEndpointFacts
{
    [Fact]
    public async Task GetWithoutASessionIs401AndAnotherBrainIs403()
    {
        await using var host = await Host.Start();
        var open = await host.Client.GetAsync(host.Item("missing"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, open.StatusCode);

        using var other = host.Request(HttpMethod.Get, "/brains/b2/storage/disk/items/missing/content", "user");
        var denied = await host.Client.SendAsync(other, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task GetSetsNosniffAndSandboxAndInlineOnlyForTextAndPng()
    {
        await using var host = await Host.Start();
        var html = host.Storage.Inner.AddFile(host.Storage.Inner.Root, "page.html", "<html>"u8);
        var png = host.Storage.Inner.AddFile(host.Storage.Inner.Root, "pic.png", "png"u8);
        var text = host.Storage.Inner.AddFile(host.Storage.Inner.Root, "note.txt", "hello"u8);

        var page = await host.Send(HttpMethod.Get, host.Item(html.Value), "user");
        var image = await host.Send(HttpMethod.Get, host.Item(png.Value), "user");
        var note = await host.Send(HttpMethod.Get, host.Item(text.Value), "user");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("nosniff", host.Header(page, "X-Content-Type-Options"));
        Assert.Equal("sandbox", host.Header(page, "Content-Security-Policy"));
        Assert.Contains("attachment", host.Header(page, "Content-Disposition"), StringComparison.Ordinal);
        Assert.Contains("inline", host.Header(image, "Content-Disposition"), StringComparison.Ordinal);
        Assert.Contains("inline", host.Header(note, "Content-Disposition"), StringComparison.Ordinal);
        Assert.Equal("hello", await note.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SeekableRangeReturns206()
    {
        await using var host = await Host.Start();
        var file = host.Storage.Inner.AddFile(host.Storage.Inner.Root, "range.bin", "0123456789abcdefghij"u8);
        using var request = host.Request(HttpMethod.Get, host.Item(file.Value), "user");
        request.Headers.Range = new RangeHeaderValue(0, 9);
        var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(10, bytes.Length);
        Assert.Equal("0123456789", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public async Task NonSeekableRangeReadsTheSlice()
    {
        await using var host = await Host.Start();
        host.Storage.Inner.Seekable = false;
        var file = host.Storage.Inner.AddFile(host.Storage.Inner.Root, "range.bin", "0123456789abcdefghij"u8);
        using var request = host.Request(HttpMethod.Get, host.Item(file.Value), "user");
        request.Headers.Range = new RangeHeaderValue(0, 9);
        var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(10, (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Length);
        Assert.Equal(0, host.Storage.LastRead?.Offset);
        Assert.Equal(10, host.Storage.LastRead?.Length);
    }

    [Fact]
    public async Task PutWithoutTheHeaderOrATicketIs403()
    {
        await using var host = await Host.Start();
        var folder = host.Storage.Inner.Root.Value;
        using var bare = host.Request(HttpMethod.Put, host.Content(folder, "a.txt"), "user");
        bare.Content = new ByteArrayContent("hi"u8.ToArray());
        var missingHeader = await host.Client.SendAsync(bare, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, missingHeader.StatusCode);

        using var headerOnly = host.Request(HttpMethod.Put, host.Content(folder, "a.txt"), "user");
        headerOnly.Headers.Add("X-Upload", "1");
        headerOnly.Content = new ByteArrayContent("hi"u8.ToArray());
        var missingTicket = await host.Client.SendAsync(headerOnly, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, missingTicket.StatusCode);

        var ticket = await host.Ticket(folder, "a.txt", null);
        host.Clock.Now += TimeSpan.FromMinutes(11);
        using var expired = host.Upload(host.Content(folder, "a.txt"), ticket, "late"u8.ToArray());
        var rejected = await host.Client.SendAsync(expired, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
    }

    [Fact]
    public async Task PutStreamsFiveMegabytesInChunks()
    {
        await using var host = await Host.Start();
        var folder = host.Storage.Inner.Root.Value;
        var ticket = await host.Ticket(folder, "big.bin", null);
        var payload = new byte[5 * 1024 * 1024];
        payload[0] = 1;
        payload[^1] = 2;
        using var request = host.Upload(host.Content(folder, "big.bin"), ticket, payload);
        var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.InRange(host.Storage.MaxWrite, 1, StorageContentGateway.CopyBuffer);
        var saved = await host.Storage.Inner.List(host.Storage.Inner.Root, new ListQuery(), TestContext.Current.CancellationToken);
        var file = Assert.Single(saved.Items, item => item.Name == "big.bin");
        Assert.Equal(payload.Length, file.Size);
    }

    [Fact]
    public async Task PutOverTheLimitIs413WithNoStorageCall()
    {
        await using var host = await Host.Start();
        var raw = await host.Raw(host.Content(host.Storage.Inner.Root.Value, "huge.bin"), StorageContentGateway.MaxBytes + 1);
        Assert.Contains("413", raw, StringComparison.Ordinal);
        Assert.Contains("File is too large.", raw, StringComparison.Ordinal);
        Assert.Empty(host.Storage.Inner.Calls);
    }

    [Fact]
    public async Task AbortedPutRemovesThePartialAndRecordsFailure()
    {
        await using var host = await Host.Start();
        var folder = host.Storage.Inner.Root.Value;
        var ticket = await host.Ticket(folder, "cut.bin", null);
        using var request = host.Upload(host.Content(folder, "cut.bin"), ticket, "partial"u8.ToArray());
        request.Headers.Add("X-Test-Abort", "1");
        var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(host.Uploads.Done, done => done.Result is OperationResult.Failed { Code: "aborted" });
        Assert.True(host.Storage.Aborts >= 1);
        var saved = await host.Storage.Inner.List(host.Storage.Inner.Root, new ListQuery(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(saved.Items, item => item.Name == "cut.bin");
    }

    [Fact]
    public async Task DeniedExpiredOrMismatchedPlanTicketsAre403()
    {
        await using var host = await Host.Start();
        _ = host.Storage.Inner.AddFile(host.Storage.Inner.Root, "notes.txt", "hello"u8);
        var folder = host.Storage.Inner.Root.Value;
        var plain = await host.Ticket(folder, "notes.txt", null);
        using var conflict = host.Upload(host.Content(folder, "notes.txt"), plain, "x"u8.ToArray());
        var rejected = await host.Client.SendAsync(conflict, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var deniedId = json.RootElement.GetProperty("planId").GetString()!;
        host.Book.Deny(deniedId);
        var denied = await host.Send(HttpMethod.Post, host.TicketPath(folder, "notes.txt", deniedId), "user");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("Nothing was changed.", await denied.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var again = await host.Ticket(folder, "notes.txt", null);
        using var second = host.Upload(host.Content(folder, "notes.txt"), again, "y"u8.ToArray());
        var secondConflict = await host.Client.SendAsync(second, TestContext.Current.CancellationToken);
        using var secondJson = JsonDocument.Parse(await secondConflict.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var expiredId = secondJson.RootElement.GetProperty("planId").GetString()!;
        host.Book.Expire(expiredId);
        using var expiredTicket = host.Request(HttpMethod.Post, host.TicketPath(folder, "notes.txt", expiredId), "user");
        var expired = await host.Client.SendAsync(expiredTicket, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);

        var approved = host.Book.Park("b1", "disk", host.Storage.Inner.Root, "notes.txt");
        host.Book.MarkStarted(approved.PlanId);
        Assert.True(host.Book.Approve(approved.PlanId, CapabilityCallers.Of("ada", "ada", "b1")));
        var ticket = await host.Ticket(folder, "notes.txt", approved.PlanId);
        using var mismatch = host.Upload(host.Content(folder, "other.txt"), ticket, "z"u8.ToArray());
        var wrong = await host.Client.SendAsync(mismatch, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private Host(WebApplication app, HttpClient client, Watched storage, Clock clock)
        {
            _app = app;
            Client = client;
            Storage = storage;
            Clock = clock;
            Book = app.Services.GetRequiredService<UploadPlanBook>();
            Uploads = app.Services.GetRequiredService<ContentUploadLog>();
        }

        public HttpClient Client { get; }
        public Watched Storage { get; }
        public Clock Clock { get; }
        public UploadPlanBook Book { get; }
        public ContentUploadLog Uploads { get; }

        public static async Task<Host> Start()
        {
            var storage = new Watched(new FakeStorage("Disk"));
            var clock = new Clock();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ApplicationName = typeof(StorageContentEndpointFacts).Assembly.GetName().Name,
                ContentRootPath = Path.GetTempPath(),
            });
            builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, "true");
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddDataProtection();
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddSingleton(new UploadPlanBook());
            builder.Services.AddSingleton(new ContentUploadLog());
            builder.Services.AddKeyedSingleton<IIdentity>(Capability.Identity.V1Id, new FakeIdentity());
            builder.Services.AddSingleton<Func<BrainId, string, CancellationToken, Task<IStorage>>>((_, _, _) => Task.FromResult<IStorage>(storage));
            builder.Services.AddSingleton<StorageContentGateway>();
            var app = builder.Build();
            app.Use(async (http, next) =>
            {
                if (http.Request.Headers.ContainsKey("X-Test-Abort")) { http.Request.Body = new CutStream(http.Request.Body); }
                if (http.Request.Headers.TryGetValue("X-Test-Caller", out var mode))
                {
                    var principalId = mode == "app" ? "app:files" : "ada";
                    var identity = new ClaimsIdentity([new Claim(SessionClaims.Principal, principalId)], "TestAuth");
                    http.User = new ClaimsPrincipal(identity);
                }
                await next(http);
            });
            StorageContentEndpoints.Map(app);
            await app.StartAsync();
            var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            return new Host(app, client, storage, clock);
        }

        public string Item(string id) => _app is not null ? "/brains/b1/storage/disk/items/" + Uri.EscapeDataString(id) + "/content" : "";

        public string Content(string folder, string name)
            => _app is not null ? "/brains/b1/storage/disk/items/" + Uri.EscapeDataString(folder) + "/content/" + Uri.EscapeDataString(name) : "";

        public string TicketPath(string folder, string name, string? planId)
        {
            if (_app is null) { throw new InvalidOperationException(); }
            var path = "/brains/b1/storage/disk/items/" + Uri.EscapeDataString(folder) + "/upload-ticket?name=" + Uri.EscapeDataString(name);
            return string.IsNullOrEmpty(planId) ? path : path + "&planId=" + Uri.EscapeDataString(planId);
        }

        public async Task<string> Ticket(string folder, string name, string? planId)
        {
            var response = await Send(HttpMethod.Post, TicketPath(folder, name, planId), "user");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return json.RootElement.GetProperty("ticket").GetString()!;
        }

        public HttpRequestMessage Request(HttpMethod method, string path, string? caller)
        {
            var message = new HttpRequestMessage(method, path);
            if (caller is not null) { message.Headers.Add("X-Test-Caller", caller); }
            if (_app is null) { throw new InvalidOperationException(); }
            return message;
        }

        public HttpRequestMessage Upload(string path, string ticket, byte[] body, string caller = "user")
        {
            var message = Request(HttpMethod.Put, path + "?ticket=" + Uri.EscapeDataString(ticket), caller);
            message.Headers.Add("X-Upload", "1");
            message.Content = new ByteArrayContent(body);
            return message;
        }

        public Task<HttpResponseMessage> Send(HttpMethod method, string path, string? caller)
            => Client.SendAsync(Request(method, path, caller), TestContext.Current.CancellationToken);

        public string Header(HttpResponseMessage response, string name)
        {
            ArgumentNullException.ThrowIfNull(response);
            if (_app is null) { throw new InvalidOperationException(); }
            if (response.Headers.TryGetValues(name, out var values)) { return values.First(); }
            return response.Content.Headers.TryGetValues(name, out var content) ? content.First() : "";
        }

        public async Task<string> Raw(string path, long length)
        {
            var uri = Client.BaseAddress ?? throw new InvalidOperationException("The host has no address.");
            using var tcp = new System.Net.Sockets.TcpClient();
            await tcp.ConnectAsync(uri.Host, uri.Port, TestContext.Current.CancellationToken);
            await using var stream = tcp.GetStream();
            var head = "PUT " + path + " HTTP/1.1\r\nHost: " + uri.Host + ":" + uri.Port
                + "\r\nX-Test-Caller: user\r\nContent-Length: " + length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), TestContext.Current.CancellationToken);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeIdentity : IIdentity
    {
        public Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken = default) => Task.FromResult<Account?>(null);
        public Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken = default) => Task.FromResult<Account?>(null);
        public Task EnsureAccount(Account account, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken = default) => Task.FromResult<BrainProvision?>(null);
        public Task<IReadOnlyList<BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BrainEntry>>([]);
        public Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken = default) => Task.FromResult(BrainKind.Personal);
        public Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
        {
            if (brainScopeId == "b1")
            {
                return Task.FromResult<Member?>(new Member
                {
                    PrincipalId = principalId,
                    AccountId = "account1",
                    BrainId = brainScopeId,
                    Role = MemberRole.Owner,
                    DisplayName = "User",
                    JoinedAt = DateTimeOffset.UtcNow,
                });
            }
            return Task.FromResult<Member?>(null);
        }
        public Task<Member?> EnsureMember(Member member, CancellationToken cancellationToken = default) => Task.FromResult<Member?>(member);
        public Task RemoveMember(string brainScopeId, string accountId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemovePrincipalMember(string brainScopeId, string principalId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CutStream(Stream inner) : Stream
    {
        private int _left = 4;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (_left <= 0) { throw new IOException("cut"); }
            var n = inner.Read(buffer[..Math.Min(buffer.Length, _left)]);
            _left -= n;
            return n == 0 ? throw new IOException("cut") : n;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_left <= 0) { throw new IOException("cut"); }
            var n = await inner.ReadAsync(buffer[..Math.Min(buffer.Length, _left)], cancellationToken);
            _left -= n;
            return n == 0 ? throw new IOException("cut") : n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Watched(FakeStorage inner) : IStorage
    {
        private readonly object _gate = new();
        public FakeStorage Inner { get; } = inner;
        public int MaxWrite { get; private set; }
        public int Aborts { get; private set; }
        public ByteRange? LastRead { get; private set; }
        public WritePlan? LastPlan { get; private set; }

        public Task<StorageDescriptor> Describe(CancellationToken ct = default) => Inner.Describe(ct);
        public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default) => Inner.Roots(ct);
        public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default) => Inner.List(folder, query, ct);
        public Task<Item> Stat(ItemId id, CancellationToken ct = default) => Inner.Stat(id, ct);
        public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default)
        {
            LastRead = range;
            return Inner.Read(id, range, ct);
        }
        public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default) => Inner.Write(plan, ct);
        public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default) => Inner.Search(query, ct);
        public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default) => Inner.Ancestors(id, ct);
        public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default) => Inner.OpenRead(item, range, ct);
        public async Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default)
        {
            LastPlan = plan;
            var opened = await Inner.OpenWrite(plan, ct);
            return new Measured(opened, count =>
            {
                lock (_gate) { if (count > MaxWrite) { MaxWrite = count; } }
            }, () => Aborts++);
        }

        private sealed class Measured(StorageWriteStream inner, Action<int> measure, Action aborted) : StorageWriteStream(new Counting(inner.Content, measure))
        {
            protected override async Task<ItemId> CommitCore(CancellationToken ct) => await inner.Commit(ct);
            protected override async Task AbortCore(CancellationToken ct)
            {
                aborted();
                await inner.Abort(ct);
            }
        }

        private sealed class Counting(Stream inner, Action<int> measure) : Stream
        {
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => inner.CanSeek;
            public override bool CanWrite => inner.CanWrite;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }
            public override void Flush() => inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void SetLength(long value) => inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count)
            {
                measure(count);
                inner.Write(buffer, offset, count);
            }
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                measure(buffer.Length);
                return inner.WriteAsync(buffer, cancellationToken);
            }
        }
    }
}
