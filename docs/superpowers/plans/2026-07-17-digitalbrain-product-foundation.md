# DigitalBrain Product Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship one real DigitalBrain vertical slice in which an owner connects Google, reads Gmail, asks AI for a bounded summary, sees the result in Flutter, approves a reply, and causes at most one Gmail send attempt with an explicit success or delivery-unknown result.

**Architecture:** Preserve the current v2 durable neuron kernel and simplify around it. The kernel owns journaling, idempotency, grants, effect approval, generic external-connection state, and protected credential custody; each module owns its typed contracts, provider protocol, runtime neuron kinds, and Aspire wiring. A module has exactly two production projects: `Module.Contracts` and `Module`; all .NET product tests live in one shared test project. The Gmail assistant is the first bounded INO operation: edge/auth invokes one typed operation, the operation composes deterministic connector and bounded-model calls, and mutations pass through the existing effect gate. This plan does not add a general scripting runtime.

**Tech Stack:** .NET 11, Orleans Journaling, Aspire 13.x, ASP.NET Core Data Protection, `Microsoft.Extensions.AI`, xUnit, Flutter/Dart, CodeGraph, Grok CLI.

## Global Constraints

- Follow `CLAUDE.md` and apply the five steps in order: question, delete, simplify, accelerate, automate.
- Do not replace the current v2 kernel with a historical kernel.
- Use CodeGraph before editing any symbol and inspect its blast radius.
- Use Context7 before package API changes; if its quota is unavailable, use official Microsoft or provider documentation and record that fallback in the task notes.
- Run `aspire doctor` and `aspire list resources` at the beginning and end of every task. The task-specific command blocks below supplement this gate; they do not replace it.
- Use TDD. Run the smallest owning test project after each change and the exact root command `dotnet test --logger "console;verbosity=minimal"` at every checkpoint.
- Never use `dotnet test --filter`.
- Do not put access tokens, refresh tokens, authorization codes, client secrets, or plaintext OAuth state in journals, receipts, logs, projections, or exception messages.
- Keep explicit module registration. Do not add reflection scanning, a second plugin runtime, a service bus, or a second discovery system.
- Keep one external effect rail: propose, approve or decline, claim once, execute once, journal the provider receipt.
- Do not implement Salesforce, Stripe, Memory, or scripting in this plan. Their contracts are designed only after the Google vertical slice proves the module boundary.
- No source-code comments. Use names, types, tests, and small functions to express intent.
- Preserve the untracked `sources/` archaeology tree and never stage it.
- This plan is temporary working documentation and should be deleted after the implementation is accepted.

## Archaeology Decision

| Codebase | Evidence | Decision |
|---|---:|---|
| current v2 | 18 projects, 125 test methods, smallest durable invocation path | Keep as the kernel base |
| `sources/brain_from_master` | 26 projects, 583 test methods | Port typed Google contracts, OAuth threat tests, capability IDs, Aspire secret wiring, and selected memory tests |
| `sources/Projects/digitalbrain` | 9 projects, 728 C# files, 155 test methods | Revisit only for INO parser/scenario tests and Flutter performance behavior |
| `sources/Projects/ino` | 88 projects, 872 test methods | Reuse test-harness and discovery test ideas, not its topology |
| `sources/Projects/v3` | clean Contracts + implementation capsules, five simulations | Reuse the two-project module rule and real-silo simulation style |
| `sources/Projects/final` | 16 projects, 45 test methods | Reuse no kernel code; selected INO validation cases may inform a later scripting plan |
| `sources/Projects/v4` | no meaningful test evidence | Do not use |
| `sources/Projects/self-improving` | two test methods and prototype paths | Do not use |

## Target Project Graph

The accepted first-slice graph is:

```text
kernel/
  Brain.Contracts
  Brain.Client
  Brain.Kernel
modules/
  Google.Contracts
  Google
  AI.Contracts
  AI
  Flutter.Contracts
  Flutter
hosts/
  Brain.Kernel.Host
  DigitalBrain.AppHost
edge/
  Brain.Mcp
tests/
  DigitalBrain.Tests
workspace/
  Flutter application
```

`Brain.Kernel` absorbs the generic Connections module. `DigitalBrain.Tests` absorbs the SDK test helpers and both current test projects. `Flutter` absorbs Workspace and the UI gateway. Service defaults are folded into the kernel host. Web, Behaviors, and the current Salesforce prototype leave the active solution after their retained behavior is either covered elsewhere or explicitly discarded.

## Grok Orchestration Contract

Codex remains accountable for architecture, test selection, review, integration, and commits. Grok receives one bounded production implementation only after Codex has written and observed the failing test. Codex performs characterization-only tasks, mechanical moves, and deletions directly; do not pay the orchestration cost when no production behavior is being designed.

For each Grok-assisted task:

1. Codex uses CodeGraph and writes the failing test.
2. Codex runs the owning test project and records the expected failure.
3. Codex invokes Grok with `--permission-mode acceptEdits`, `--no-subagents`, and a bounded turn count. The installed Grok CLI rejects `--no-subagents` together with `--check`, so Codex owns all verification.
4. Grok may edit only the files named in that task. Grok may not edit tests, this plan, `CLAUDE.md`, package versions, or git history.
5. Codex inspects `git diff`, uses CodeGraph on changed symbols, removes unnecessary code, and runs the owning tests.
6. Codex runs the root test gate, Aspire doctor, and relevant resource/log checks before committing.
7. Codex commits one coherent task. Grok never commits.

Example invocation:

```powershell
grok --cwd . --single "Implement Task 3 from docs/superpowers/plans/2026-07-17-digitalbrain-product-foundation.md. The failing tests already exist. Edit only the production files listed by Task 3. Do not edit tests, plans, package versions, CLAUDE.md, or git history. Implement the minimum code that makes the owning test project pass. Run the owning test project without a test filter and report changed files and verification." --permission-mode acceptEdits --no-subagents --max-turns 8
```

---

### Task 1: Lock the green v2 kernel behavior before moving projects

**Files:**
- Create: `tests/Brain.KernelTests/ProductFoundationBaselineTests.cs`
- Modify: `tests/Brain.KernelTests/KernelKindsConfigurator.cs`
- Test: `tests/Brain.KernelTests/ProductFoundationBaselineTests.cs`

**Interfaces:**
- Consumes: `INeuron`, `NeuronInvocation`, `NeuronReceipt`, `EffectKind`
- Produces: executable characterization of replay, revision, grant, and effect invariants

- [ ] **Step 1: Record the clean baseline**

Run:

```powershell
dotnet test tests/Brain.KernelTests/Brain.KernelTests.csproj --logger "console;verbosity=minimal"
dotnet test tests/Brain.ConformanceTests/Brain.ConformanceTests.csproj --logger "console;verbosity=minimal"
```

Expected: both projects pass before new tests are added.

- [ ] **Step 2: Add green characterization tests**

Add tests that prove:

```text
repeating a command ID returns the original receipt without a second event
expected revision rejects stale writes
a grant is bound to owner, space, grantee, and allowed contract
an effect cannot be claimed before approval
an approved effect is claimed exactly once
```

Use the existing `KernelKindsConfigurator`, `TestKind`, and `ProposerKind`; do not create a second test cluster or production abstraction.

- [ ] **Step 3: Verify the characterization stays green**

Run:

```powershell
dotnet test tests/Brain.KernelTests/Brain.KernelTests.csproj --logger "console;verbosity=minimal"
```

Expected: the complete kernel test project passes.

- [ ] **Step 4: Commit the green characterization**

```powershell
git add tests/Brain.KernelTests/ProductFoundationBaselineTests.cs tests/Brain.KernelTests/KernelKindsConfigurator.cs
git commit -m "test(kernel): lock product foundation invariants"
```

### Task 2: Collapse generic Connections and test SDK into their owners

**Files:**
- Move: `modules/Brain.Modules.Connections/ConnectionKind.cs` → `kernel/Brain.Kernel/Connections/ConnectionKind.cs`
- Move: `modules/Brain.Modules.Connections/ConnectionHttp.cs` → `kernel/Brain.Kernel/Connections/ConnectionHttp.cs`
- Move: `modules/Brain.Modules.Connections/ConnectionsHosting.cs` → `kernel/Brain.Kernel/Connections/ConnectionHosting.cs`
- Move: `modules/Brain.Modules.Connections/IConnectionProvider.cs` → `kernel/Brain.Kernel/Connections/IConnectionProvider.cs`
- Delete: `modules/Brain.Modules.Connections/DevConnectionProvider.cs`
- Delete: `modules/Brain.Modules.Connections/Brain.Modules.Connections.csproj`
- Move: `modules/Brain.Modules.Sdk/BrainTest.cs` → `tests/DigitalBrain.Tests/Infrastructure/BrainTest.cs`
- Move: `modules/Brain.Modules.Sdk/FakeChatClient.cs` → `tests/DigitalBrain.Tests/TestDoubles/FakeChatClient.cs`
- Move: `modules/Brain.Modules.Sdk/FakeConnectionProvider.cs` → `tests/DigitalBrain.Tests/TestDoubles/FakeConnectionProvider.cs`
- Move: `modules/Brain.Modules.Sdk/FakeGmailProvider.cs` → `tests/DigitalBrain.Tests/TestDoubles/FakeGmailProvider.cs`
- Delete: `modules/Brain.Modules.Sdk/FakeSalesforceProvider.cs`
- Move: `modules/Brain.Modules.Sdk/FakeTimeProvider.cs` → `tests/DigitalBrain.Tests/TestDoubles/FakeTimeProvider.cs`
- Delete: `modules/Brain.Modules.Sdk/Brain.Modules.Sdk.csproj`
- Move directory: `tests/Brain.KernelTests/` → `tests/DigitalBrain.Tests/`
- Rename: `tests/DigitalBrain.Tests/Brain.KernelTests.csproj` → `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Move directory contents: `tests/Brain.ConformanceTests/` → `tests/DigitalBrain.Tests/Conformance/`
- Delete after migration: `tests/DigitalBrain.Tests/Conformance/Brain.ConformanceTests.csproj`
- Modify: `DigitalBrain.slnx`
- Modify: `hosts/Brain.Kernel.Host/Program.cs`
- Modify: project references that currently point to Connections, SDK, KernelTests, or ConformanceTests
- Delete: `modules/Brain.Modules.Salesforce/`
- Delete after recording the current green baseline: `tests/DigitalBrain.Tests/SalesforceConnectorTests.cs`
- Delete after recording the current green baseline: `tests/DigitalBrain.Tests/SalesforceReadWithoutConnectionTests.cs`
- Delete after recording the current green baseline: `tests/DigitalBrain.Tests/Conformance/SalesforceConformance.cs`

**Interfaces:**
- Consumes: current `IConnectionProvider`, `ConnectionToken`, `ProbeResult`, and test fixture behavior
- Produces: `Brain.Kernel.Connections` and one shared `DigitalBrain.Tests` assembly

- [ ] **Step 1: Create the shared test project**

Use a single project with references to the active production projects and these packages:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../kernel/Brain.Contracts/Brain.Contracts.csproj" />
    <ProjectReference Include="../../kernel/Brain.Client/Brain.Client.csproj" />
    <ProjectReference Include="../../kernel/Brain.Kernel/Brain.Kernel.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.Orleans.TestingHost" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Move generic connection code into the kernel**

Change its namespace to `Brain.Kernel.Connections`. Keep its public behavior unchanged. `AddBrainKernel` must register the connection kind once:

```csharp
public static ISiloBuilder AddBrainKernel(this ISiloBuilder silo, params INeuronKind[] kinds)
{
    silo.UseJsonJournalFormat(NeuronJournalJsonContext.Default);
    silo.Services.AddSingleton<IAttributeToFactoryMapper<NeuronStateAttribute>, NeuronStateMapper>();
    silo.AddBrainConnection();
    return silo.AddBrainKinds(kinds.Append(new EffectKind()));
}
```

- [ ] **Step 3: Move test-only infrastructure into `DigitalBrain.Tests`**

Use namespace `DigitalBrain.Tests`. Production projects must not reference the test project or contain fake providers.

- [ ] **Step 4: Delete the out-of-scope Salesforce prototype**

Record its passing tests in the task notes, then remove its host registration, project, tests, fake provider, and solution entry. The later Salesforce plan ports the stronger tested contracts and OAuth evidence from `sources/brain_from_master`; it does not preserve this weaker prototype.

- [ ] **Step 5: Keep retained tests mechanically stable**

Keep the former kernel tests at the root of `tests/DigitalBrain.Tests/` and the former conformance tests under `tests/DigitalBrain.Tests/Conformance/`. Do not spend this task reorganizing files that already compile. Later tasks place only their new tests in `Google/`, `AI/`, `Flutter/`, `Product/`, or `Architecture/`.

- [ ] **Step 6: Verify the behavior-preserving move**

Run:

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

Expected: all retained tests and theory cases pass with no failures.

- [ ] **Step 7: Commit**

```powershell
git add DigitalBrain.slnx kernel modules tests hosts edge
git commit -m "refactor(kernel): absorb connection runtime and test sdk"
```

### Task 3: Make connection authorization and credential custody safe

**Files:**
- Create: `kernel/Brain.Kernel/Connections/IConnectionTokenProtector.cs`
- Create: `kernel/Brain.Kernel/Connections/DataProtectionConnectionTokenProtector.cs`
- Create: `kernel/Brain.Kernel/Connections/ConnectionAuthorizationState.cs`
- Create: `kernel/Brain.Kernel/Connections/ConnectionSecurityOptions.cs`
- Modify: `kernel/Brain.Kernel/Connections/ConnectionKind.cs`
- Modify: `kernel/Brain.Kernel/Connections/ConnectionHosting.cs`
- Modify: `kernel/Brain.Kernel/Brain.Kernel.csproj`
- Modify: `tests/DigitalBrain.Tests/ConnectionsKindsConfigurator.cs`
- Test: `tests/DigitalBrain.Tests/ConnectionKindTests.cs`
- Test: `tests/DigitalBrain.Tests/ProductFoundationBaselineTests.cs`

**Interfaces:**
- Consumes: `NeuronAddress`, `ConnectionToken`, `IDataProtectionProvider`
- Produces: `IConnectionTokenProtector.Protect`, `IConnectionTokenProtector.Unprotect`, state-bound OAuth completion, transient token leases

- [ ] **Step 1: Add failing security tests**

Add tests for:

```text
wrong OAuth state is rejected before token exchange
expired OAuth state is rejected
replayed OAuth completion is rejected
connection journal contains neither authorization code nor plaintext token
connection projection contains no protected token
token lease is transient and allowed only to a granted same-owner neuron
token remains readable after grain reactivation with the same data-protection key ring
```

The wrong-state assertion must verify `ExchangeCodeAsync` was called zero times.

Add the known-defect regression with the moved `ConnectionsKindsConfigurator` fixture:

```csharp
[Fact]
public async Task Connection_journal_never_contains_plaintext_credentials()
{
    const string accessToken = "access-token-must-not-be-journaled";
    const string refreshToken = "refresh-token-must-not-be-journaled";
    ConnectionsKindsConfigurator.GoogleProvider.ExchangeResult =
        new ConnectionToken(accessToken, refreshToken, DateTimeOffset.UtcNow.AddHours(1));

    var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
    var start = await connection.InvokeAsync(new(
        "connection.start-auth.v1",
        "{}",
        Guid.NewGuid().ToString("N"),
        OwnerSession));
    var state = StateFrom(start.OutputJson);

    await connection.InvokeAsync(new(
        "connection.complete-auth.v1",
        JsonSerializer.Serialize(new { code = "code", state }),
        Guid.NewGuid().ToString("N"),
        OwnerSession));

    var journal = JsonSerializer.Serialize(
        (await connection.ReadEventsAsync(0, 500)).Events);
    Assert.DoesNotContain(accessToken, journal, StringComparison.Ordinal);
    Assert.DoesNotContain(refreshToken, journal, StringComparison.Ordinal);
}

private static string StateFrom(string outputJson)
{
    using var output = JsonDocument.Parse(outputJson);
    var authorizationUrl = output.RootElement.GetProperty("authorizationUrl").GetString()!;
    return new Uri(authorizationUrl).Query
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .Where(pair => Uri.UnescapeDataString(pair[0]) == "state")
        .Select(pair => Uri.UnescapeDataString(pair[1]))
        .Single();
}
```

Reset `ConnectionsKindsConfigurator.GoogleProvider` in test cleanup so static fixture state cannot leak into another test.

- [ ] **Step 2: Run the owning test project**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
```

Expected: the new security tests fail.

- [ ] **Step 3: Add a purpose-bound token protector**

Define:

```csharp
public interface IConnectionTokenProtector
{
    string Protect(NeuronAddress address, ConnectionToken token);
    ConnectionToken Unprotect(NeuronAddress address, string protectedToken);
}
```

The implementation must create a data protector with the ordered purposes:

```text
DigitalBrain.ConnectionToken.v1
owner id
space id
provider name
connection neuron id
```

Register `AddDataProtection().SetApplicationName("DigitalBrain")` and a singleton `IConnectionTokenProtector`. Development persists keys under the ignored repository-local `.digitalbrain/keys/` directory so kernel restarts can decrypt existing connections. Production requires a configured shared key-ring location and fails startup when it is absent; use the already-versioned Azure Blob Data Protection package for the first deployment target. Add a two-provider test that protects with one service provider and unprotects with another sharing the same temporary key directory.

- [ ] **Step 4: Store only protected token material**

Replace the `connection.connected` payload with:

```csharp
private sealed record ConnectedPayload(
    string ProtectedToken,
    DateTimeOffset ExpiresAt,
    string? InstanceUrl);
```

`Fold` retains `ProtectedToken`; probe and lease unprotect it only at the call boundary. Projections return state, health, fix, expiry, and suspension only.

- [ ] **Step 5: Bind OAuth completion to the start**

The authorizing event contains only a SHA-256 digest of a 32-byte random base64url state and its expiry. `connection.complete-auth.v1` requires both `code` and `state`. Compare state digests with `CryptographicOperations.FixedTimeEquals`, then exchange the code once.

- [ ] **Step 6: Make the red test green**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```powershell
git add kernel/Brain.Kernel tests/DigitalBrain.Tests
git commit -m "feat(kernel): protect external connection credentials"
```

### Task 4: Establish the two-project Google module

**Files:**
- Create: `modules/Google.Contracts/Google.Contracts.csproj`
- Create: `modules/Google.Contracts/GoogleCapabilityIds.cs`
- Create: `modules/Google.Contracts/GmailContracts.cs`
- Create: `modules/Google.Contracts/IGmailNeuron.cs`
- Rename: `modules/Brain.Modules.Google/Brain.Modules.Google.csproj` → `modules/Google/Google.csproj`
- Move current Google production files into: `modules/Google/`
- Modify: `kernel/Brain.Client/NeuronProxy.cs`
- Modify: `kernel/Brain.Contracts/NeuronEnvelope.cs`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Modify: `DigitalBrain.slnx`
- Test: `tests/DigitalBrain.Tests/Google/GoogleContractTests.cs`
- Test: `tests/DigitalBrain.Tests/Kernel/TypedProxyTests.cs`

**Interfaces:**
- Produces: `GoogleCapabilityIds`, `IGmailNeuron`, typed Gmail requests and responses, `NeuronReply<T>`
- Consumes: `INeuronContract`, `NeuronContractAttribute`, `NeuronProxy`

- [ ] **Step 1: Write contract boundary tests**

Assert that `Google.Contracts` references only `Brain.Contracts`, has no Aspire or provider SDK dependency, and exposes these IDs exactly:

```csharp
public static class GoogleCapabilityIds
{
    public const string GmailMessageRead = "google.gmail.message.read.v1";
    public const string GmailMailboxRead = "google.gmail.mailbox.read.v1";
    public const string GmailSendPropose = "google.gmail.send.propose.v1";
    public const string GmailSendExecute = "google.gmail.send.execute.v1";
    public const string GmailInboxSummarize = "google.gmail.inbox.summarize.v1";
}
```

- [ ] **Step 2: Create bounded typed contracts**

Port and simplify the proven contracts from `sources/brain_from_master/integrations/DigitalBrain.Integrations.Google.Contracts/`. Keep mailbox limits at 1–100, message IDs at 512 characters, addresses at 320, subjects at 998, and bodies at 100,000 characters for send and 1,000,000 for read.

Define:

```csharp
public interface IGmailNeuron : INeuronContract
{
    [NeuronContract(GoogleCapabilityIds.GmailMailboxRead)]
    Task<GmailMailboxPage> ReadMailboxAsync(GmailMailboxReadRequest request);

    [NeuronContract(GoogleCapabilityIds.GmailMessageRead)]
    Task<GmailMessage> ReadMessageAsync(GmailMessageReadRequest request);

    [NeuronContract(GoogleCapabilityIds.GmailSendPropose)]
    Task<NeuronReply<GmailSendProposal>> ProposeSendAsync(GmailSendProposalRequest request);

    [NeuronContract(GoogleCapabilityIds.GmailSendExecute)]
    Task<GmailSendResult> ExecuteSendAsync(GmailSendExecutionRequest request);
}
```

- [ ] **Step 3: Add typed receipt support once**

Add this client-side result type without Orleans serialization attributes because it is assembled from a `NeuronReceipt` after the wire call:

```csharp
public sealed record NeuronReply<T>(
    T Value,
    long Revision,
    string? EffectKey);
```

Update `NeuronProxy` so a method returning `Task<NeuronReply<T>>` deserializes `T` from `OutputJson` and copies `Revision` and `EffectKey` from `NeuronReceipt`.

- [ ] **Step 4: Move the Google implementation**

`Google` references `Google.Contracts`, `Brain.Kernel`, `Microsoft.Extensions.Http`, and `Aspire.Hosting`. It contains Gmail neuron kinds, Google OAuth/API clients, runtime registration, and Aspire extensions. No third Google production project is permitted. Add both Google project references to `DigitalBrain.Tests` in this task; production projects never reference the test assembly.

- [ ] **Step 5: Verify typed proxy and contract stability**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

- [ ] **Step 6: Commit**

```powershell
git add DigitalBrain.slnx kernel modules tests
git commit -m "feat(google): establish contracts and integration module"
```

### Task 5: Put Google runtime and Aspire wiring in the same package

**Files:**
- Create: `modules/Google/GoogleRuntimeExtensions.cs`
- Create: `modules/Google/GoogleAspireExtensions.cs`
- Create: `modules/Google/GoogleOptions.cs`
- Modify: `hosts/Brain.Kernel.Host/Program.cs`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Test: `tests/DigitalBrain.Tests/Google/GoogleAspireTests.cs`
- Test: `tests/DigitalBrain.Tests/Google/GoogleOAuthSecurityTests.cs`
- Test: `tests/DigitalBrain.Tests/Google/GoogleHttpProviderTests.cs`

**Interfaces:**
- Produces: `ISiloBuilder.AddDigitalBrainGoogle`, `IResourceBuilder<T>.WithDigitalBrainGoogle`
- Consumes: Google client ID, client secret, redirect URI, kernel project resource

- [ ] **Step 1: Port the high-value OAuth threat tests**

Port the relevant assertions from `sources/brain_from_master/tests/DigitalBrain.OrleansTests/Connectors/OAuthConnectorSecurityTests.cs`:

```text
authorization URL requires HTTPS, exact host, exact path, default port, and no fragment
requested scopes are exactly gmail.readonly and gmail.send
state is owner-scoped, opaque, expiring, and single-use
tampered state never reaches token exchange
denied consent does not persist credentials
provider errors do not leak response bodies containing credentials
production rejects a non-HTTPS redirect URI
```

- [ ] **Step 2: Add Aspire topology tests**

Assert that the Google module declares three parameters:

```text
google-client-id: secret
google-client-secret: secret
google-redirect-uri: non-secret
```

Assert that the kernel resource receives:

```text
DigitalBrain__Google__ClientId
DigitalBrain__Google__ClientSecret
DigitalBrain__Google__RedirectUri
```

- [ ] **Step 3: Implement the Aspire extension**

Use:

```csharp
public static IResourceBuilder<T> WithDigitalBrainGoogle<T>(
    this IResourceBuilder<T> kernel)
    where T : IResourceWithEnvironment
{
    var builder = kernel.ApplicationBuilder;
    var clientId = builder.AddParameter("google-client-id", secret: true);
    var clientSecret = builder.AddParameter("google-client-secret", secret: true);
    var redirectUri = builder.AddParameter(
        "google-redirect-uri",
        "http://localhost:5311/oauth/callback/google",
        publishValueAsDefault: true);

    return kernel
        .WithEnvironment("DigitalBrain__Google__ClientId", clientId)
        .WithEnvironment("DigitalBrain__Google__ClientSecret", clientSecret)
        .WithEnvironment("DigitalBrain__Google__RedirectUri", redirectUri);
}
```

Give the kernel host a stable development HTTP endpoint on port `5311`; it owns `/oauth/callback/google`, validates the returned state, invokes `connection.complete-auth.v1`, and redirects to the Flutter UI with a status only. The callback response and redirect must never contain the authorization code or token material.

- [ ] **Step 4: Implement runtime registration**

`AddDigitalBrainGoogle` must fail closed when configuration is partially present. A completely absent configuration registers a deterministic development provider only in the `Development` or `Test` environment. Production never silently substitutes a fake provider.

- [ ] **Step 5: Cover the real HTTP adapter**

Use a stub `HttpMessageHandler` and test token exchange, profile probe, mailbox paging, message parsing, send encoding, timeouts, non-success responses, malformed JSON, and cancellation. No CI test calls Google.

- [ ] **Step 6: Bind Gmail mutation to the kernel effect rail**

`google.gmail.send.propose.v1` validates and journals a safe summary, returns the kernel effect key, and never calls the provider. `google.gmail.send.execute.v1` requires that exact approved effect key, claims it for the same owner and operation, and calls the provider at most once. A successful call journals only the provider message ID. A timeout after claim becomes `delivery-unknown` and is never retried automatically because Gmail does not offer a transactional idempotency key for message send. Add tests for decline, wrong owner, wrong effect key, replay, success, and delivery-unknown.

- [ ] **Step 7: Verify**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

- [ ] **Step 8: Commit**

```powershell
git add modules/Google hosts tests/DigitalBrain.Tests
git commit -m "feat(google): add secure runtime and aspire integration"
```

### Task 6: Establish the two-project AI module

**Files:**
- Create: `modules/AI.Contracts/AI.Contracts.csproj`
- Create: `modules/AI.Contracts/AiCapabilityIds.cs`
- Create: `modules/AI.Contracts/ITextGenerationNeuron.cs`
- Rename: `modules/Brain.Modules.Ai/Brain.Modules.Ai.csproj` → `modules/AI/AI.csproj`
- Move current AI files into: `modules/AI/`
- Create: `modules/AI/AiAspireExtensions.cs`
- Modify: `hosts/Brain.Kernel.Host/Program.cs`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Test: `tests/DigitalBrain.Tests/AI/AiContractTests.cs`
- Test: `tests/DigitalBrain.Tests/AI/TextGenerationTests.cs`
- Test: `tests/DigitalBrain.Tests/AI/AiAspireTests.cs`

**Interfaces:**
- Produces: `ai.text.generate.v1`, `ITextGenerationNeuron`, `WithDigitalBrainAI`
- Consumes: `IChatClient`, bounded prompt and output options

- [ ] **Step 1: Add typed AI contracts**

Define one capability:

```csharp
public static class AiCapabilityIds
{
    public const string TextGenerate = "ai.text.generate.v1";
}

public sealed record TextGenerationRequest(
    string Instruction,
    string Input,
    int MaximumOutputTokens = 512);

public sealed record TextGenerationResult(string Text);

public interface ITextGenerationNeuron : INeuronContract
{
    [NeuronContract(AiCapabilityIds.TextGenerate)]
    Task<TextGenerationResult> GenerateAsync(TextGenerationRequest request);
}
```

Validate instruction and input lengths before model invocation. Preserve current idempotency and model-tier tests.

Add an architecture test proving `AI.Contracts` references only `Brain.Contracts` and has no Aspire, HTTP, model-provider, or runtime dependency. Add both AI project references to `DigitalBrain.Tests`.

- [ ] **Step 2: Move Ollama Aspire composition into the AI module**

`AI` exposes `AddDigitalBrainAI` for silo/runtime registration and `WithDigitalBrainAI` for AppHost composition. `Brain.Kernel.Host/Program.cs` calls the runtime extension. `WithDigitalBrainAI` adds the Ollama resource/model and wires its endpoint to the kernel. `AppHost.cs` calls the Aspire extension and contains no AI-provider details.

- [ ] **Step 3: Verify**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

- [ ] **Step 4: Commit**

```powershell
git add DigitalBrain.slnx modules/AI.Contracts modules/AI hosts tests
git commit -m "feat(ai): establish contracts runtime and aspire module"
```

### Task 7: Establish the Flutter UI contract and module

**Files:**
- Create: `modules/Flutter.Contracts/Flutter.Contracts.csproj`
- Move: `modules/Brain.Modules.Workspace/Blocks.cs` → `modules/Flutter.Contracts/UiDocument.cs`
- Move: `modules/Brain.Modules.Workspace/IWindow.cs` → `modules/Flutter.Contracts/IWindowNeuron.cs`
- Create: `modules/Flutter/Flutter.csproj`
- Move: `modules/Brain.Modules.Workspace/WindowKind.cs` → `modules/Flutter/WindowKind.cs`
- Move: `modules/Brain.Modules.Workspace/FeedKind.cs` → `modules/Flutter/FeedKind.cs`
- Move UI gateway production files from: `edge/Brain.UiGateway/`
- Create: `modules/Flutter/FlutterAspireExtensions.cs`
- Modify: `hosts/Brain.Kernel.Host/Program.cs`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Modify: `workspace/lib/blocks/block_view.dart`
- Modify: `workspace/lib/gateway/`
- Test: `tests/DigitalBrain.Tests/Flutter/UiContractTests.cs`
- Test: `tests/DigitalBrain.Tests/Flutter/FlutterGatewayTests.cs`
- Test: `workspace/test/blocks/block_view_test.dart`

**Interfaces:**
- Produces: versioned transport-neutral `UiDocument`, `UiBlock`, `UiAction`, `IWindowNeuron`
- Consumes: JSON over the existing gateway transport

- [ ] **Step 1: Freeze the minimal UI wire contract**

The C# contracts contain no Flutter types:

```csharp
public sealed record UiDocument(int Version, IReadOnlyList<UiBlock> Blocks);

public sealed record UiBlock(
    string Kind,
    string? Text = null,
    string? Label = null,
    string? Value = null,
    UiAction? Action = null,
    IReadOnlyList<UiBlock>? Children = null);

public sealed record UiAction(
    string Contract,
    string Target,
    string InputJson);
```

Allow only `text`, `heading`, `list`, `card`, `button`, and `status` in v1. Reject unknown kinds, excessive nesting, oversized text, and malformed action JSON.

Add an architecture test proving `Flutter.Contracts` references only `Brain.Contracts` and has no ASP.NET, Aspire, transport, or Flutter dependency. Add both Flutter project references to `DigitalBrain.Tests`.

- [ ] **Step 2: Move workspace runtime and gateway into one module**

`Flutter` owns the window/feed kinds, authenticated gateway endpoints, and Aspire wiring. `Flutter.Contracts` owns only DTOs and neuron interfaces. The Dart app remains under `workspace/` and mirrors the versioned JSON contract. `Brain.Kernel.Host/Program.cs` calls `AddDigitalBrainFlutter` and maps the gateway extension. `AppHost.cs` calls `WithDigitalBrainFlutter`; it contains no Flutter command, port, or gateway details.

The gateway derives owner and space from the authenticated server session, never from `UiAction.InputJson`. It checks the target grant before invoking a contract and rejects missing authentication, cross-owner targets, ungranted contracts, oversized input, and replayed mutation command IDs. Add tests for each rejection before moving the endpoints.

- [ ] **Step 3: Add cross-language fixtures**

Store canonical JSON fixtures under `workspace/test/fixtures/ui_document_v1/`. C# tests serialize them; Dart tests parse and render them. This is the compatibility gate instead of introducing code generation.

- [ ] **Step 4: Verify**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
flutter test
dotnet test --logger "console;verbosity=minimal"
```

Run `flutter test` from `workspace`.

- [ ] **Step 5: Commit**

```powershell
git add DigitalBrain.slnx modules/Flutter.Contracts modules/Flutter workspace tests hosts edge
git commit -m "feat(flutter): establish ui contracts runtime and aspire module"
```

### Task 8: Build the Gmail → AI → Flutter product neuron

**Files:**
- Create: `modules/Google.Contracts/GmailInboxSummaryContracts.cs`
- Create: `modules/Google.Contracts/IGmailAssistantOperation.cs`
- Create: `modules/Google/GmailInboxSummaryKind.cs`
- Modify: `modules/Google/GoogleRuntimeExtensions.cs`
- Test: `tests/DigitalBrain.Tests/Product/GmailAssistantJourneyTests.cs`

**Interfaces:**
- Produces: typed bounded INO operation `google.gmail.inbox.summarize.v1`
- Consumes: `IGmailNeuron`, `ITextGenerationNeuron`, `IWindowNeuron`, effect approval rail

- [ ] **Step 1: Write the end-to-end test first**

The real Orleans test cluster uses deterministic Google and AI adapters and asserts this sequence:

```text
owner starts and completes Google authorization
Gmail summary reads at most ten mailbox entries
message bodies passed to AI are bounded to 32 KiB total
AI is invoked once for a repeated command ID
one UiDocument is rendered into the owner's main window
send proposal returns an effect key and does not call Gmail send
approval enables one execution
execution calls Gmail send at most once and journals the provider message ID on success
a timeout becomes delivery-unknown and repeated execution does not call Gmail again
another owner cannot read the first owner's connection or window
```

Assert that capability discovery exposes `IGmailAssistantOperation` as the single typed operation entry for this journey.

- [ ] **Step 2: Verify the journey fails**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
```

Expected: failure because `GmailInboxSummaryKind` is absent.

- [ ] **Step 3: Implement the minimum orchestration**

The kind is the first bounded INO operation and performs only:

```text
read mailbox
read bounded message bodies
invoke AI text generation
render a UiDocument
return a typed summary receipt
```

The rendered document may contain a typed `google.gmail.send.propose.v1` action. Proposal, owner approval, and `google.gmail.send.execute.v1` then use the Google contracts and kernel effect rail completed in Task 5. The operation does not bypass or duplicate that rail.

It does not introduce a general INO parser, workflow engine, queue, saga framework, planner, autonomous loop, or second effect system.

- [ ] **Step 4: Verify all product gates**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
Push-Location workspace
flutter test
Pop-Location
dotnet test --logger "console;verbosity=minimal"
aspire doctor
aspire list resources
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add modules/Google.Contracts modules/Google tests/DigitalBrain.Tests
git commit -m "feat(product): summarize gmail and approve replies"
```

### Task 9: Delete superseded topology and prove the real Aspire composition

**Files:**
- Delete: `modules/Brain.Modules.Workspace/`
- Delete: `modules/Brain.Modules.Ai/`
- Delete: `edge/Brain.UiGateway/`
- Move: `hosts/DigitalBrain.ServiceDefaults/Extensions.cs` → `hosts/Brain.Kernel.Host/ServiceDefaults.cs`
- Delete: `hosts/DigitalBrain.ServiceDefaults/`
- Remove from active solution after retained tests are accounted for: `modules/Brain.Modules.Web/`
- Remove from active solution after retained tests are accounted for: `modules/Brain.Modules.Behaviors/`
- Delete after recording the retained assertions: `tests/DigitalBrain.Tests/WebKindTests.cs`
- Delete after recording the retained assertions: `tests/DigitalBrain.Tests/WebKindsConfigurator.cs`
- Delete after recording the retained assertions: `tests/DigitalBrain.Tests/Conformance/WebConformance.cs`
- Delete after recording the retained assertions: `tests/DigitalBrain.Tests/BehaviorCompilerTests.cs`
- Delete after recording the retained assertions: `tests/DigitalBrain.Tests/BehaviorKindTests.cs`
- Delete after recording the retained assertions: `tests/DigitalBrain.Tests/BehaviorsKindsConfigurator.cs`
- Delete after recording the retained assertions: `tests/DigitalBrain.Tests/Conformance/BehaviorConformance.cs`
- Modify: `hosts/Brain.Kernel.Host/Brain.Kernel.Host.csproj`
- Modify: `hosts/Brain.Kernel.Host/Program.cs`
- Modify: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `DigitalBrain.slnx`
- Create: `tests/DigitalBrain.Tests/Architecture/ProjectTopologyTests.cs`

**Interfaces:**
- Produces: the target project graph and one healthy Aspire product composition

- [ ] **Step 1: Add the project topology guard**

The test enumerates active `.csproj` entries in `DigitalBrain.slnx` and asserts:

```text
the only active Google projects are Google.Contracts and Google
the only active AI projects are AI.Contracts and AI
the only active Flutter projects are Flutter.Contracts and Flutter
there is one shared product test project
no active project name contains Modules.Sdk, Modules.Connections, UiGateway, or ServiceDefaults
```

- [ ] **Step 2: Fold the retained host defaults and delete superseded projects**

Move the existing OpenTelemetry, health-check, service-discovery, and HTTP-resilience registrations into `Brain.Kernel.Host/ServiceDefaults.cs`; move its package references into the host project; keep `AddServiceDefaults` and `MapDefaultEndpoints` behavior covered by a host startup test. Then delete the project boundary. Do not retain compatibility wrappers or forwarding assemblies. Git history and `sources/` preserve archaeology.

- [ ] **Step 3: Run complete verification**

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --logger "console;verbosity=minimal"
```

From `workspace`:

```powershell
flutter analyze
flutter test
```

Then:

```powershell
aspire doctor
aspire run
```

Use `aspire list resources` and Aspire resource inspection to verify kernel, MCP, Flutter, Ollama, and docs are healthy. Exercise the deterministic Google development journey and inspect logs to confirm no secrets appear.

- [ ] **Step 4: Commit**

```powershell
git add -A -- DigitalBrain.slnx hosts modules edge tests workspace
git commit -m "refactor(product): delete superseded module topology"
```

## Acceptance Gate

The slice is complete only when:

- The root .NET suite and Flutter suite pass.
- Aspire doctor passes.
- All required Aspire resources are healthy.
- Google app secrets are declared by the Google module and are not hard-coded.
- Google contracts have no Aspire, HTTP, or provider SDK references.
- AI contracts have no Aspire, HTTP, model-provider, or runtime references.
- Flutter contracts have no ASP.NET, Aspire, transport, or Flutter references.
- Plaintext credentials appear nowhere in journal events, projections, receipts, logs, or errors.
- Capability discovery lists stable typed Google, AI, and Flutter contracts.
- Gmail read and AI summary are replay-safe.
- Gmail send requires approval, invokes the provider at most once, and exposes an explicit delivery-unknown state after an ambiguous timeout.
- Owner isolation is proven by tests.
- The active project graph matches the target graph.
- No new generic runtime, reflection scanner, queue, workflow engine, or scripting engine was introduced.

## Follow-up Plans After Acceptance

Create separate implementation plans in this order:

1. `Memory.Contracts` + `Memory`, porting only owner-scoped facts, bounded lexical recall, correction, forgetting, ETags, and audit behavior from `brain_from_master`.
2. `Salesforce.Contracts` + `Salesforce`, proving the two-project pattern against a second OAuth provider before extracting any additional shared OAuth abstraction.
3. `Stripe.Contracts` + `Stripe`, placing every monetary mutation behind the existing effect approval rail.
4. `Scripting.Contracts` + `Scripting`, porting the tested INO parser/scenario runner while forbidding arbitrary in-process C# execution.

Each follow-up module uses the same shared `DigitalBrain.Tests` project until measured build or ownership pain justifies a split.

## Continuation Prompt

```text
You are the lead Codex orchestrator for DigitalBrain. Start from the repository root.

Execute docs/superpowers/plans/2026-07-17-digitalbrain-product-foundation.md exactly, task by task, starting with Task 1. Do not redesign the product and do not implement Salesforce, Stripe, Memory, or scripting in this run.

Follow CLAUDE.md. Use the superpowers:executing-plans skill for execution, superpowers:test-driven-development before production changes, systematic debugging for failures, and verification-before-completion before every completion claim. Create an isolated worktree and a codex/ prefixed branch before editing.

At the beginning of every task:
1. Use CodeGraph to inspect the exact symbols and blast radius.
2. Query current package/framework documentation through Context7. If Context7 quota is unavailable, use official provider documentation and record the fallback.
3. Run Aspire doctor and inspect current resources.
4. Write the failing test yourself and run the owning test project without --filter to prove the expected failure.

Use Grok CLI as a bounded implementation worker after the failing test exists for Tasks 3 through 8. Invoke it headlessly with --permission-mode acceptEdits, --no-subagents, and --max-turns 8. Do not add --check because the installed Grok CLI rejects it together with --no-subagents. Give Grok only the current task, the exact allowed production files, and the observed failing-test output. Grok must not edit tests, plans, CLAUDE.md, package versions, or git history and must not commit. Codex performs Tasks 1, 2, and 9 directly because they are characterization, mechanical movement, or deletion.

After Grok returns:
1. Inspect git diff.
2. Use CodeGraph on every changed symbol.
3. Delete unnecessary abstractions and code.
4. Run the owning test project.
5. Run dotnet test --logger "console;verbosity=minimal".
6. Run Aspire doctor and inspect relevant resources/logs.
7. Commit the coherent task only when every gate passes.

Never stage sources/. Preserve unrelated user changes. Stop and report concrete evidence if a plan assumption is invalid; otherwise continue through all tasks without asking for routine confirmation.
```
