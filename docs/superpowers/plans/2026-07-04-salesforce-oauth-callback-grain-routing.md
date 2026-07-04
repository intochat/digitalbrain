# Salesforce OAuth Callback Grain-Routing (MULTIUSER Stage S1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the cross-replica OAuth callback race (P1 in `docs/CONTINUATION-MULTIUSER-IDENTITY.md`) by moving Salesforce token exchange and pending-state validation into `SalesforceAuthNeuron.CompleteOAuthAsync`, so the callback always resolves to the single Orleans activation that started the flow, regardless of which Kernel replica's Kestrel instance receives the HTTP request.

**Architecture:** `Program.cs`'s `/salesforce-callback` minimal-API endpoint becomes a pure parse-and-route layer: it reads the query string, builds a `SalesforceOAuthCallback` value, and calls `ISalesforceAuthNeuron.CompleteOAuthAsync` on the well-known `"salesforce-auth-main"` grain. Orleans delivers that grain call to the single live activation no matter which replica's HTTP frontend accepted the request. All pending-state reads, state/nonce validation, and the Salesforce token exchange move inside the grain method — the endpoint no longer touches `IPackConfigStore` at all. A new optional `HttpMessageHandler` seam on `SalesforceClientFactory.ExchangeAuthorizationCodeAsync` lets tests fake the Salesforce token endpoint without real network I/O (avoids Windows HTTP.sys ACL issues with `HttpListener` and avoids adding new packages).

**Tech Stack:** .NET 11 / C#, Orleans 10.2.1-preview (grains, TestingHost), ASP.NET Core minimal APIs, xUnit.

## Global Constraints

- Grain keys stay exactly as they are today (`"salesforce-auth-main"`) — no per-user keying in this stage. Per-user keying is MULTIUSER stage S3, out of scope here.
- Every new type that crosses a grain-interface boundary gets `[GenerateSerializer]` plus explicit sequential `[property: Id(n)]` on every member, starting at 0 — non-negotiable per the design doc's own risk notes (`docs/CONTINUATION-MULTIUSER-IDENTITY.md` §8), and matches this codebase's existing non-`Synapse` cross-grain records (`UserSessionState`, `DbSchemaModel` in `DigitalBrain.Core/Synapse.cs`).
- Tokens, refresh tokens, and pending PKCE material never enter a journal — only the encrypted `IPackConfigStore` holds them (invariant I3). Do not add journaled synapse types for OAuth completion in this stage; that belongs to the shared `OAuthFlowNeuron` abstraction in a later stage (S4), not here.
- The old direct-store-IO code path in `Program.cs` must be **deleted**, not commented out or left dead — this is the plan's explicit acceptance bar.
- No new NuGet packages. The `HttpMessageHandler` test seam uses only BCL types already available via the implicit `System.Net.Http` using.
- Every step that changes code must compile under `dotnet build Brain.slnx` before being considered done.

---

### Task 1: Testable HTTP seam for the Salesforce token exchange

**Files:**
- Create: `DigitalBrain.Salesforce.Tests/FakeSalesforceTokenHandler.cs`
- Modify: `DigitalBrain.Salesforce/SalesforceClientFactory.cs:124-176` (`ExchangeAuthorizationCodeAsync`), `:299-325` (`RequestTokenAsync`)
- Test: `DigitalBrain.Salesforce.Tests/SalesforceClientFactoryTests.cs`

**Interfaces:**
- Produces: `FakeSalesforceTokenHandler(string accessToken, string instanceUrl, string? refreshToken = null) : HttpMessageHandler` with `int RequestCount { get; }` and `static string ExtractQueryValue(string url, string key)`. Tasks 2 and 4 both use this type.
- Produces: `SalesforceClientFactory.ExchangeAuthorizationCodeAsync(IReadOnlyDictionary<string, string> values, string code, string redirectUri, HttpMessageHandler? tokenEndpointHandler = null)` — the 4th parameter is new; existing 3-argument call sites remain source-compatible.

- [ ] **Step 1: Create the fake token handler test double**

Create `DigitalBrain.Salesforce.Tests/FakeSalesforceTokenHandler.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Salesforce.Tests;

public sealed class FakeSalesforceTokenHandler(string accessToken, string instanceUrl, string? refreshToken = null)
    : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;

        var payload = new Dictionary<string, string?>
        {
            ["access_token"] = accessToken,
            ["instance_url"] = instanceUrl
        };
        if (refreshToken is not null)
            payload["refresh_token"] = refreshToken;

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        });
    }

    public static string ExtractQueryValue(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?');
        foreach (var pair in query.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == key)
                return Uri.UnescapeDataString(parts[1]);
        }

        throw new InvalidOperationException($"Query parameter '{key}' not found in '{url}'.");
    }
}
```

- [ ] **Step 2: Write the failing test for the new handler seam**

Add to `DigitalBrain.Salesforce.Tests/SalesforceClientFactoryTests.cs` (inside the existing `SalesforceClientFactoryTests` class, after `CreateForceClientAsync_Missing_Config_Throws_Clear_Error`):

```csharp
    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_Uses_Provided_Handler_Instead_Of_Real_Network_Call()
    {
        var handler = new FakeSalesforceTokenHandler("fake-access-token", "https://fake.my.salesforce.com", "fake-refresh-token");

        var result = await SalesforceClientFactory.ExchangeAuthorizationCodeAsync(
            new Dictionary<string, string>
            {
                [SalesforceClientFactory.ClientIdKey] = "connected-app-id",
                [SalesforceClientFactory.ClientSecretKey] = "connected-app-secret",
                [SalesforceClientFactory.LoginUrlKey] = "https://test.salesforce.com",
                [SalesforceClientFactory.OAuthCodeVerifierKey] = "verifier-1"
            },
            "auth-code-1",
            "http://localhost:8081/salesforce-callback",
            handler);

        Assert.Equal("fake-access-token", result[SalesforceClientFactory.AccessTokenKey]);
        Assert.Equal("https://fake.my.salesforce.com", result[SalesforceClientFactory.InstanceUrlKey]);
        Assert.Equal("fake-refresh-token", result[SalesforceClientFactory.RefreshTokenKey]);
        Assert.Equal(1, handler.RequestCount);
    }
```

- [ ] **Step 3: Run the test to verify it fails to compile**

Run: `dotnet test DigitalBrain.Salesforce.Tests/DigitalBrain.Salesforce.Tests.csproj --filter "FullyQualifiedName~ExchangeAuthorizationCodeAsync_Uses_Provided_Handler"`

Expected: build error — `ExchangeAuthorizationCodeAsync` does not take a 4th argument.

- [ ] **Step 4: Add the optional handler parameter**

In `DigitalBrain.Salesforce/SalesforceClientFactory.cs`, change the `ExchangeAuthorizationCodeAsync` signature (line 124-127) and its call to `RequestTokenAsync` (line 153):

```csharp
    public static async Task<IReadOnlyDictionary<string, string>> ExchangeAuthorizationCodeAsync(
        IReadOnlyDictionary<string, string> values,
        string code,
        string redirectUri,
        HttpMessageHandler? tokenEndpointHandler = null)
    {
```

```csharp
        var token = await RequestTokenAsync(TokenEndpoint(loginUrl), form, tokenEndpointHandler)
            .ConfigureAwait(false);
```

Change `RequestTokenAsync` (line 299-303) to accept and use the optional handler:

```csharp
    private static async Task<SalesforceTokenResponse> RequestTokenAsync(
        string tokenEndpoint,
        IReadOnlyDictionary<string, string> form,
        HttpMessageHandler? handler = null)
    {
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        using var content = new FormUrlEncodedContent(form);
```

Leave the rest of `RequestTokenAsync`'s body (lines 306-325) unchanged. The other two call sites of `RequestTokenAsync` (`CreateForceClientAsync` line 62-70, `CreateOAuthForceClientAsync` line 263-270) do not pass a handler and are unaffected — they keep using `new HttpClient()`.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test DigitalBrain.Salesforce.Tests/DigitalBrain.Salesforce.Tests.csproj --filter "FullyQualifiedName~ExchangeAuthorizationCodeAsync_Uses_Provided_Handler"`

Expected: PASS (1 test).

- [ ] **Step 6: Commit**

```bash
git add DigitalBrain.Salesforce.Tests/FakeSalesforceTokenHandler.cs DigitalBrain.Salesforce.Tests/SalesforceClientFactoryTests.cs DigitalBrain.Salesforce/SalesforceClientFactory.cs
git commit -m "feat(salesforce): add testable HTTP seam for token exchange"
```

---

### Task 2: `CompleteOAuthAsync` on `SalesforceAuthNeuron`

**Files:**
- Modify: `DigitalBrain.Core/Synapse.cs` (append after line 421)
- Modify: `DigitalBrain.Salesforce/ISalesforceAuthNeuron.cs`
- Modify: `DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs`
- Test: `DigitalBrain.Salesforce.Tests/SalesforceAuthNeuronTests.cs`

**Interfaces:**
- Consumes: `FakeSalesforceTokenHandler` from Task 1 (same assembly, no new using needed since both are in `DigitalBrain.Salesforce.Tests` namespace).
- Produces: `SalesforceOAuthCallback(string? Code, string? State, string? Error, string? ErrorDescription, string FallbackRedirectUri)` and `SalesforceOAuthCallbackResult(bool Success, string Title, string Message)` in `DigitalBrain.Core`. Produces `ISalesforceAuthNeuron.CompleteOAuthAsync(SalesforceOAuthCallback callback) : Task<SalesforceOAuthCallbackResult>`. Task 3 (Program.cs) and Task 4 (cross-silo test) both call this method and construct these two records.

- [ ] **Step 1: Add the wire types to Core**

Append to the end of `DigitalBrain.Core/Synapse.cs` (after `PerformKernelSelfUpdate` on line 421):

```csharp

// Salesforce OAuth callback completion (MULTIUSER S1: grain-routed callback, replaces direct
// Program.cs store IO so the completion always reaches the activation that started the flow).
[GenerateSerializer]
public record SalesforceOAuthCallback(
    [property: Id(0)] string? Code,
    [property: Id(1)] string? State,
    [property: Id(2)] string? Error,
    [property: Id(3)] string? ErrorDescription,
    [property: Id(4)] string FallbackRedirectUri);

[GenerateSerializer]
public record SalesforceOAuthCallbackResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Title,
    [property: Id(2)] string Message);
```

- [ ] **Step 2: Add the method to the grain interface**

In `DigitalBrain.Salesforce/ISalesforceAuthNeuron.cs`, replace the whole file:

```csharp
using DigitalBrain.Core;

namespace DigitalBrain.Salesforce;

public interface ISalesforceAuthNeuron : INeuron, IHandle<Signal>
{
    Task<SalesforceOAuthCallbackResult> CompleteOAuthAsync(SalesforceOAuthCallback callback);
}
```

- [ ] **Step 3: Write the failing happy-path test**

Add to `DigitalBrain.Salesforce.Tests/SalesforceAuthNeuronTests.cs`. First, register the fake token handler in `ConfigureSilo` (replace the existing override):

```csharp
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<HttpMessageHandler>(
                new FakeSalesforceTokenHandler("fake-access-token", "https://fake.my.salesforce.com"));
        });
```

Then add these two tests after `OAuthStart_Pending_State_Survives_Concurrent_Credential_Write`:

```csharp
    [Fact]
    public async Task CompleteOAuthAsync_With_Valid_Code_And_State_Stores_Tokens_And_Succeeds()
    {
        var writer = Grain<ISalesforceConnectedAppConfigWriter>("salesforce-connected-app-writer-complete");
        await writer.StoreConnectedAppConfigAsync();

        var auth = Grain<ISalesforceAuthNeuron>("salesforce-auth-test-complete");
        await auth.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
        {
            ["sessionId"] = "session-complete",
            ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:8081/salesforce-callback"
        })
        { Receiver = new NeuronId("salesforce-auth-test-complete") });

        var outgoing = await auth.GetOutgoingTimelineAsync();
        var authUrlSignal = Assert.Single(outgoing.OfType<Signal>(), item => item.Name == SalesforceSignals.AuthUrl);
        var url = Assert.IsType<string>(authUrlSignal.Props["url"]);
        var state = FakeSalesforceTokenHandler.ExtractQueryValue(url, "state");

        var result = await auth.CompleteOAuthAsync(new SalesforceOAuthCallback(
            Code: "auth-code-1",
            State: state,
            Error: null,
            ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/salesforce-callback"));

        Assert.True(result.Success);
        Assert.Equal("Salesforce connected", result.Title);

        var stored = await writer.ReadPackAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.PackName);
        Assert.Equal("fake-access-token", stored[SalesforceClientFactory.AccessTokenKey]);

        var pendingAfter = await writer.ReadPackAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.OAuthPendingPackName);
        Assert.False(pendingAfter.ContainsKey(SalesforceClientFactory.OAuthStateKey));
    }

    [Fact]
    public async Task CompleteOAuthAsync_With_Mismatched_State_Fails_Without_Exchanging_Code()
    {
        var writer = Grain<ISalesforceConnectedAppConfigWriter>("salesforce-connected-app-writer-mismatch");
        await writer.StoreConnectedAppConfigAsync();

        var auth = Grain<ISalesforceAuthNeuron>("salesforce-auth-test-mismatch");
        await auth.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
        {
            ["sessionId"] = "session-mismatch",
            ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:8081/salesforce-callback"
        })
        { Receiver = new NeuronId("salesforce-auth-test-mismatch") });

        var result = await auth.CompleteOAuthAsync(new SalesforceOAuthCallback(
            Code: "auth-code-1",
            State: "wrong-state",
            Error: null,
            ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/salesforce-callback"));

        Assert.False(result.Success);
        Assert.Equal("The callback state did not match the pending login.", result.Message);
    }
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test DigitalBrain.Salesforce.Tests/DigitalBrain.Salesforce.Tests.csproj --filter "FullyQualifiedName~CompleteOAuthAsync"`

Expected: build error — `ISalesforceAuthNeuron` has no `CompleteOAuthAsync` implementation yet on `SalesforceAuthNeuron` (interface member not implemented).

- [ ] **Step 5: Implement `CompleteOAuthAsync`**

In `DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs`, add this method to the `SalesforceAuthNeuron` class (after `StartOAuthAsync`, before `PublishCredentialFormAsync`):

```csharp
    public async Task<SalesforceOAuthCallbackResult> CompleteOAuthAsync(SalesforceOAuthCallback callback)
    {
        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            return new SalesforceOAuthCallbackResult(
                false,
                "Salesforce login failed",
                $"{callback.Error}: {callback.ErrorDescription}".TrimEnd(':', ' '));
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            return new SalesforceOAuthCallbackResult(
                false,
                "Salesforce login failed",
                "The callback did not include an authorization code.");
        }

        var store = ServiceProvider.GetRequiredService<IPackConfigStore>();
        var values = await store.GetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.PackName);
        var pending = await store.GetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.OAuthPendingPackName);

        if (pending.TryGetValue(SalesforceClientFactory.OAuthStateKey, out var expectedState) &&
            !string.IsNullOrWhiteSpace(expectedState) &&
            !string.Equals(expectedState, callback.State, StringComparison.Ordinal))
        {
            return new SalesforceOAuthCallbackResult(
                false,
                "Salesforce login failed",
                "The callback state did not match the pending login.");
        }

        var redirectUri = values.TryGetValue(SalesforceClientFactory.RedirectUriKey, out var storedRedirectUri)
            ? storedRedirectUri
            : callback.FallbackRedirectUri;

        try
        {
            var exchangeValues = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            if (pending.TryGetValue(SalesforceClientFactory.OAuthCodeVerifierKey, out var pendingCodeVerifier))
                exchangeValues[SalesforceClientFactory.OAuthCodeVerifierKey] = pendingCodeVerifier;

            var handler = ServiceProvider.GetService<HttpMessageHandler>();
            var tokenValues = await SalesforceClientFactory.ExchangeAuthorizationCodeAsync(exchangeValues, callback.Code, redirectUri, handler);
            var merged = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in tokenValues)
                merged[key] = value;

            await store.SetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.PackName, merged);
            await store.SetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.OAuthPendingPackName, new Dictionary<string, string>());

            await Broadcast(new Signal("PackConfigured", new Dictionary<string, object?>
            {
                ["pack"] = SalesforceClientFactory.PackName,
                ["scope"] = SalesforceClientFactory.DefaultScope
            }));
            await Broadcast(new Signal(SalesforceSignals.AuthCompleted, new Dictionary<string, object?>
            {
                ["provider"] = "salesforce",
                ["pack"] = SalesforceClientFactory.PackName,
                ["scope"] = SalesforceClientFactory.DefaultScope
            }));

            return new SalesforceOAuthCallbackResult(
                true,
                "Salesforce connected",
                "You can close this browser tab and return to DigitalBrain.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Salesforce OAuth callback failed.");
            return new SalesforceOAuthCallbackResult(false, "Salesforce login failed", ex.GetBaseException().Message);
        }
    }
```

This preserves both existing post-completion broadcasts (`"PackConfigured"`, consumed by `InoNeuron`; `SalesforceSignals.AuthCompleted`, currently unconsumed but kept for parity with Google's `AuthCompleted` signal) exactly as `Program.cs` fired them today, just from inside the grain instead of via a throwaway `IIngressNeuron`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test DigitalBrain.Salesforce.Tests/DigitalBrain.Salesforce.Tests.csproj --filter "FullyQualifiedName~CompleteOAuthAsync"`

Expected: PASS (2 tests).

- [ ] **Step 7: Run the full Salesforce test project to check for regressions**

Run: `dotnet test DigitalBrain.Salesforce.Tests/DigitalBrain.Salesforce.Tests.csproj`

Expected: PASS (all tests, including the 4 pre-existing `SalesforceAuthNeuronTests` and the `SalesforceClientFactoryTests`).

- [ ] **Step 8: Commit**

```bash
git add DigitalBrain.Core/Synapse.cs DigitalBrain.Salesforce/ISalesforceAuthNeuron.cs DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs DigitalBrain.Salesforce.Tests/SalesforceAuthNeuronTests.cs
git commit -m "feat(salesforce): add CompleteOAuthAsync to SalesforceAuthNeuron"
```

---

### Task 3: Route the `Program.cs` callback endpoint through the grain

**Files:**
- Modify: `DigitalBrain.Kernel/Program.cs:310-391`

**Interfaces:**
- Consumes: `ISalesforceAuthNeuron.CompleteOAuthAsync(SalesforceOAuthCallback)` from Task 2.

- [ ] **Step 1: Replace the endpoint body**

In `DigitalBrain.Kernel/Program.cs`, replace lines 310-391 (the entire `app.MapGet(SalesforceClientFactory.DefaultCallbackPath, ...)` block) with:

```csharp
app.MapGet(SalesforceClientFactory.DefaultCallbackPath, async (
    HttpRequest request,
    IGrainFactory grains) =>
{
    var callback = new SalesforceOAuthCallback(
        Code: request.Query["code"].FirstOrDefault(),
        State: request.Query["state"].FirstOrDefault(),
        Error: request.Query["error"].FirstOrDefault(),
        ErrorDescription: request.Query["error_description"].FirstOrDefault(),
        FallbackRedirectUri: SalesforceCallbackUri(request));

    var auth = grains.GetGrain<ISalesforceAuthNeuron>("salesforce-auth-main");
    var result = await auth.CompleteOAuthAsync(callback);

    return Results.Content(
        SalesforceCallbackPage(result.Title, result.Message),
        "text/html",
        statusCode: result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
});
```

This deletes the direct `IPackConfigStore` reads/writes, the direct `SalesforceClientFactory.ExchangeAuthorizationCodeAsync` call, and the throwaway `IIngressNeuron` broadcast — all of that now lives in `CompleteOAuthAsync` (Task 2). `SalesforceCallbackUri(request)` and `SalesforceCallbackPage(title, message)` (the two static helpers at the bottom of the file) are unchanged and still used.

- [ ] **Step 2: Build the Kernel project**

Run: `dotnet build DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`

Expected: builds with no errors. (The removed `DigitalBrain.Core.Config.IPackConfigStore packConfigStore` and `ILogger<Program> callbackLogger` parameters are gone from this handler; confirm no other code in `Program.cs` referenced them from this closure.)

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build Brain.slnx`

Expected: builds with no errors across all projects.

- [ ] **Step 4: Commit**

```bash
git add DigitalBrain.Kernel/Program.cs
git commit -m "refactor(salesforce): route OAuth callback endpoint through the grain"
```

---

### Task 4: Cross-silo regression test (S1 acceptance criterion)

**Files:**
- Create: `DigitalBrain.Salesforce.Tests/SalesforceOAuthCrossSiloTests.cs`

**Interfaces:**
- Consumes: `FakeSalesforceTokenHandler` (Task 1), `ISalesforceAuthNeuron.CompleteOAuthAsync` (Task 2).

- [ ] **Step 1: Write the cross-silo regression test**

Create `DigitalBrain.Salesforce.Tests/SalesforceOAuthCrossSiloTests.cs`:

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel.Config;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public class SalesforceOAuthCrossSiloTests : NeuronTestBase
{
    protected override short InitialSilosCount => 2;

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<HttpMessageHandler>(
                new FakeSalesforceTokenHandler("fake-access-token", "https://fake.my.salesforce.com"));
        });

    [Fact]
    public async Task Callback_Delivered_Through_Different_Silo_Frontend_Still_Completes()
    {
        var silo0Grains = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<IGrainFactory>();
        var silo1Grains = ((InProcessSiloHandle)Cluster.Silos[1]).SiloHost.Services.GetRequiredService<IGrainFactory>();

        var authOnSilo0 = silo0Grains.GetGrain<ISalesforceAuthNeuron>("salesforce-auth-main");
        var startingSiloIdentity = await authOnSilo0.GetSiloIdentityAsync();

        await authOnSilo0.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
        {
            ["sessionId"] = "session-cross-silo",
            ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
            [SalesforceClientFactory.ClientIdKey] = "connected-app-id",
            [SalesforceClientFactory.ClientSecretKey] = "connected-app-secret",
            [SalesforceClientFactory.LoginUrlKey] = "https://test.salesforce.com",
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:8081/salesforce-callback"
        })
        { Receiver = new NeuronId("salesforce-auth-main") });

        var outgoing = await authOnSilo0.GetOutgoingTimelineAsync();
        var authUrlSignal = Assert.Single(outgoing.OfType<Signal>(), item => item.Name == SalesforceSignals.AuthUrl);
        var authorizeUrl = Assert.IsType<string>(authUrlSignal.Props["url"]);
        var state = FakeSalesforceTokenHandler.ExtractQueryValue(authorizeUrl, "state");

        // Different IGrainFactory, simulating the callback landing on a different Kernel replica
        // than the one that served the "Login via Salesforce" request.
        var authOnSilo1 = silo1Grains.GetGrain<ISalesforceAuthNeuron>("salesforce-auth-main");
        var completingSiloIdentity = await authOnSilo1.GetSiloIdentityAsync();

        // Proves Orleans routed both calls to the SAME single activation regardless of entry silo —
        // this is the property that fixes P1 (previously the callback bypassed the grain entirely).
        Assert.Equal(startingSiloIdentity, completingSiloIdentity);

        var result = await authOnSilo1.CompleteOAuthAsync(new SalesforceOAuthCallback(
            Code: "fake-authorization-code",
            State: state,
            Error: null,
            ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/salesforce-callback"));

        Assert.True(result.Success);
        Assert.Equal("Salesforce connected", result.Title);
    }
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test DigitalBrain.Salesforce.Tests/DigitalBrain.Salesforce.Tests.csproj --filter "FullyQualifiedName~Callback_Delivered_Through_Different_Silo_Frontend_Still_Completes"`

Expected: PASS. If it fails with a serialization error mentioning `SalesforceOAuthCallback` or `SalesforceOAuthCallbackResult`, re-check Step 1 of Task 2 — every property must have `[property: Id(n)]` with no gaps.

- [ ] **Step 3: Commit**

```bash
git add DigitalBrain.Salesforce.Tests/SalesforceOAuthCrossSiloTests.cs
git commit -m "test(salesforce): add cross-silo regression test for OAuth callback routing"
```

---

### Task 5: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Run the full Salesforce test project**

Run: `dotnet test DigitalBrain.Salesforce.Tests/DigitalBrain.Salesforce.Tests.csproj`

Expected: PASS, all tests green.

- [ ] **Step 2: Build and run the full test suite for the solution**

Run: `dotnet build Brain.slnx` then `dotnet test Brain.slnx`

Expected: PASS, no regressions in any other project (Gateway, Ino, Ui, etc. — none of them reference the deleted `Program.cs` code path).

- [ ] **Step 3: Aspire sanity check**

Per project convention, validate via Aspire tooling before calling this done:
- Run `mcp__aspire__doctor` to confirm the local Aspire/dotnet toolchain is healthy.
- Run `mcp__aspire__list_apphosts` and `mcp__aspire__select_apphost` for `NeuroOSPrototype.AppHost`.
- Start the app (`aspire run` or the equivalent MCP tool) and confirm the Kernel resource comes up healthy with no startup exceptions.
- Hit `GET /salesforce-callback?error=access_denied&error_description=smoke-test` against the running Kernel endpoint and confirm it returns HTTP 400 with the "Salesforce login failed" page — this exercises the new parse-and-route endpoint end-to-end without needing real Salesforce credentials.
- Stop the app host when done.

- [ ] **Step 4: Update CONTINUITY.md**

Add a dated entry noting Stage S1 (grain-routed Salesforce OAuth callback) shipped, referencing this plan file and the acceptance test in Task 4.

---

## Out of scope (deliberately deferred to later stages)

- Per-user grain keying (`{userId}` instead of `"salesforce-auth-main"`) — MULTIUSER S3.
- DataProtection-encrypted `state` (D-MU2) and the generic `/oauth/{provider}/callback` endpoint — MULTIUSER S4.
- Journaled `OAuthFlowStarted`/`OAuthCompleted`/`OAuthFailed` synapse types — part of the shared `OAuthFlowNeuron` abstraction, MULTIUSER S4.
- Google OAuth — untouched by this plan; MULTIUSER S4 builds it on the shared flow from scratch.
- Whole-repo cleanup waves (D1-D4 in `docs/CONTINUATION-CLEANUP-SIMPLIFICATION.md`) — separate plan, run after this one per D-CL6's sequencing (S1 → cleanup → S2-S5).
