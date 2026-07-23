# Lean Architecture Refactoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan.

**Goal:** Make the current PR branch match DigitalBrain's ratified architecture with the smallest defensible runtime: durable MAF sessions owned by Neurons, one concrete official-SDK MCP runtime behind semantic provider Neurons, truthful mutation recovery, deterministic Aspire composition, and a production AppHost that runs the VitePress website.

**Architecture:** Orleans remains the only durability and identity authority. Microsoft Agent Framework remains the only agent/orchestration engine, with Microsoft.Extensions.AI messages as the public substrate. `DigitalBrain.Integrations.Mcp` remains as the southbound mechanics module but loses its imitation client interfaces; Google and Salesforce retain provider vocabulary and policy only. `hosts/DigitalBrain.Mcp` remains the separate northbound Neuron exposure host and is not refactored in this work.

**Tech Stack:** .NET 10, Aspire 13.4.6, Orleans 10.2, Microsoft Agent Framework 1.13.0, Microsoft.Extensions.AI, ModelContextProtocol 1.4.1, VitePress 1.6.x, xUnit v3, CodeGraph 1.5.0.

## Global Constraints

- Runtime planning ground is `23c7ea5d0e0ded65e1ca0511ce6269bc05be3f2a`, merge-base `312ee5993b2b0c4e3e2a145c6f8205f5c1058465`, with a clean worktree. This approved plan is committed immediately above that runtime ground; execution records its own start HEAD before Task 1 and treats no other movement as expected.
- At the start of execution and before every slice, run:

  ```powershell
  git rev-parse HEAD
  git status --porcelain
  git merge-base master HEAD
  ```

  Stop and inspect if the branch moved unexpectedly or if unrelated changes appeared.
- Before changing a library-facing call, query current documentation and inspect the installed API. Context7 is the first choice; if its quota is unavailable, use official source, NuGet package metadata, Aspire CLI documentation, and a compiler probe. A signature is not accepted until the owning project compiles.
- Re-run that documentation gate for Aspire, Orleans, Microsoft Agent Framework, ModelContextProtocol, Vite/VitePress, Google hosted MCP, and Salesforce hosted MCP before the slice that changes each integration. Record any Context7 quota failure rather than silently substituting memory.
- After Task 1, use the live CodeGraph MCP before every runtime slice to inspect the affected files, direct dependencies, callers, and callees; confirm the result with `rg`. Do not create a second checked-in architecture inventory that can drift from the graph.
- Every behavioral slice is red-green-refactor: add one public-seam proof, run it and observe the expected failure, implement only enough to pass, run the owning test project, then refactor.
- Do not test private helpers when the behavior can be proven through a Neuron contract, the official MCP HTTP protocol, an Orleans restart, the Aspire application model, or a live Aspire resource.
- Do not add compatibility shims. There are no known external consumers of the internal MCP interfaces or the new sample command shape.
- Do not add roles, tenants, provider routers, account registries, generic workflow abstractions, test-only production seams, or raw MCP escape hatches.
- Keep `Concurrent` and `GroupChat`. Keep Tasks independent from AI. Keep the current AI-to-Tasks.Contracts dependency direction and the `IWorker` bridge.
- Keep `src/DigitalBrain.Security`, `src/DigitalBrain.Integrations.Mcp`, `src/DigitalBrain.Integrations.Mcp.Aspire.Hosting`, and `src/DigitalBrain.Aspire.Hosting`; deepen them instead of redistributing their mechanics into providers.
- Leave `hosts/DigitalBrain.Mcp` behavior unchanged. Only compiler fallout from shared package changes may touch it.
- Do not add a custom npm-install path for the website. `AddViteApp` owns its package install and process lifecycle.
- Never run an AppHost with `dotnet run`. Use `aspire start --isolated --non-interactive`; never pass `--no-build`.
- Every green slice gets its own commit. Immediately before each commit:

  ```powershell
  git diff --cached --name-only
  git diff --cached
  git diff --cached --check
  ```

  Then answer explicitly:

  1. What was added without a current consumer?
  2. What was claimed without verification?
  3. What changed outside the intended slice?

  The acceptable answer is either “nothing” with evidence or a concrete reason to unstage/rework the slice.
- Do not push.

## Selected Combination and Compatibility

This is a deliberate combination of the three approved directions:

1. **Deletion-first boundary refinement:** remove the imitation MCP client interfaces, public redirect seam, private session wrapper, provider fake-client layer, shallow GroupChat workflow wrapper, split direct-session state helpers, and stray AppHost probe file.
2. **Failure-path and authority hardening:** add write rollback, account-bound OAuth state, full safety-relevant tool drift detection, bounded post-fence reconciliation, durable direct-session restart, workflow drift rejection, and real cancellation.
3. **Composition and hosting refinement:** make CodeGraph an AppHost build invariant, use the official Vite resource lifecycle, keep one Azure storage profile per brain, preserve silo/client secret projection, and retain distinct northbound and southbound MCP boundaries.

Public runtime Neuron contracts `IGmail`, `ISalesforce`, `IAgent`, `IGroupChat`, `IWorker`, and Tasks remain. The intentionally breaking public changes are removal of consumerless `IMcpAuthorizationRedirect` and addition of `GmailAccount` to the sample command. Internal MAF session envelopes move to a new format and reject old payloads with an explicit reset/migration error; no compatibility decoder is retained because there is no deployed consumer requiring one.

The highest-risk slices are MCP OAuth/transport and MAF restart recovery, so each is proven against the real official SDK or a real Orleans host restart before cleanup. Website composition and build tooling come first because every later live Aspire proof depends on them. Expected payoff is fewer indirection types, fewer protocol sessions per Gmail read, no provider-specific mechanics copies, and one durable lifecycle owner for each kind of state.

---

## Task 1: Make every Aspire build refresh the same CodeGraph served through MCP

**Files:**

- Create: `tools/codegraph.mjs`
- Modify: `Directory.Build.targets`
- Modify: `.mcp.json`
- Modify: `.codex/config.toml`
- Modify: `.gitignore`
- Modify: `CLAUDE.md`

**Resulting interface:**

```text
node tools/codegraph.mjs init
node tools/codegraph.mjs sync
node tools/codegraph.mjs status
node tools/codegraph.mjs serve --mcp
```

The wrapper is the single CodeGraph lifecycle entry point for MSBuild and MCP. It pins CodeGraph 1.5.0, installs the package into ignored `.config/codegraph-tool` only when absent, and invokes the package's official `npm-shim.js` with the current Node executable. This narrow bootstrap is required by the observed Windows npm 11 failure where `npx @colbymchenry/codegraph` installs the package but cannot launch its generated `codegraph.cmd`.

- [ ] Record the ground commands from Global Constraints.
- [ ] Establish the red proof by running an AppHost build with diagnostic verbosity and showing that `InitCodeGraph` is skipped because the current target explicitly excludes AppHost projects:

  ```powershell
  dotnet build hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --no-restore --verbosity:diagnostic |
      Select-String 'InitCodeGraph|CodeGraph'
  ```

- [ ] Establish the second red proof:

  ```powershell
  npx --yes '@colbymchenry/codegraph@1.5.0' status
  ```

  Observe the current Windows launch failure rather than treating the existing `.codegraph/codegraph.db` as proof that the command works.
- [ ] Implement `tools/codegraph.mjs` with exactly three responsibilities:

  1. Find the repository root from `import.meta.url`.
  2. Install `@colbymchenry/codegraph@1.5.0` under `.config/codegraph-tool` with `npm install --prefix <tool-home> --no-package-lock --no-save` only when `node_modules/@colbymchenry/codegraph/npm-shim.js` is absent.
  3. Spawn `process.execPath` with the resolved `npm-shim.js` and all caller arguments, inherit stdio, and propagate a non-zero exit code.

  Use `npm.cmd` on Windows and `npm` elsewhere. Do not implement indexing, MCP, or process supervision in the wrapper.
- [ ] Replace the sentinel target with:

  ```xml
  <Target Name="RefreshCodeGraph"
          BeforeTargets="Build"
          Condition="'$(IsAspireHost)' == 'true'">
    <Exec Condition="Exists('$(MSBuildThisFileDirectory).codegraph\codegraph.db')"
          Command="node &quot;$(MSBuildThisFileDirectory)tools/codegraph.mjs&quot; sync"
          WorkingDirectory="$(MSBuildThisFileDirectory)" />
    <Exec Condition="!Exists('$(MSBuildThisFileDirectory).codegraph\codegraph.db')"
          Command="node &quot;$(MSBuildThisFileDirectory)tools/codegraph.mjs&quot; init"
          WorkingDirectory="$(MSBuildThisFileDirectory)" />
  </Target>
```

  Remove `ContinueOnError`, the sentinel property, the sentinel touch, and the warning. Existing indexes are synchronized on every AppHost build; a clean source copy is initialized and indexed on its first AppHost build. A requested architecture index that failed is a failed AppHost build.
- [ ] Point the `codegraph` entries in `.mcp.json` and `.codex/config.toml` at `node tools/codegraph.mjs serve --mcp`. Keep the other MCP entries byte-for-byte unchanged.
- [ ] Ignore `.config/codegraph-tool/`. Update only the now-false CodeGraph launcher and build-maintenance statements in `CLAUDE.md`.
- [ ] Run the green build proof:

  ```powershell
  dotnet build hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --no-restore --verbosity:minimal
  node tools/codegraph.mjs status
  ```

  Confirm the build output contains the CodeGraph refresh and `status` reports the repository index current.
- [ ] Prove the configured process is an MCP server, not only a CLI:

  ```powershell
  $messages = @(
    '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"digitalbrain-verification","version":"1.0"}}}',
    '{"jsonrpc":"2.0","method":"notifications/initialized"}',
    '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
  ) -join "`n"
  $mcpOutput = $messages | node tools/codegraph.mjs serve --mcp
  $mcpOutput
  ```

  Require successful responses for ids 1 and 2 and a non-empty tool list.
- [ ] Run `git diff --check`, stage only this slice, answer the three commit questions, and commit:

  ```powershell
  git commit -m "build: refresh CodeGraph on every Aspire build"
  ```

---

## Task 2: Add the VitePress website to the production AppHost application model

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj`
- Create: `tests/DigitalBrain.HostTests/ProductionTopology.cs`

**Resulting AppHost code:**

```csharp
builder.AddViteApp("website", "../../docs")
    .WithExternalHttpEndpoints();
```

- [ ] Add `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj` as a project reference in `DigitalBrain.HostTests.csproj`.
- [ ] Add `ProductionTopology.WebsiteIsAnExternalViteResource` using `DistributedApplicationTestingBuilder.CreateAsync<Projects.DigitalBrain_AppHost>()`. Assert:

  ```csharp
  var website = Assert.Single(appHost.Resources, resource => resource.Name == "website");
  var endpoint = Assert.Single(website.Annotations.OfType<EndpointAnnotation>());

  Assert.Equal("http", endpoint.Name);
  Assert.True(endpoint.IsExternal);
  ```

- [ ] Run the test and observe it fail because the production model has no `website` resource:

  ```powershell
  dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj `
      --filter "FullyQualifiedName~ProductionTopology.WebsiteIsAnExternalViteResource" `
      --logger "console;verbosity=minimal"
  ```

- [ ] Add centrally managed `Aspire.Hosting.JavaScript` version `13.4.6` to `Directory.Packages.props` and an unversioned `PackageReference` to the production AppHost project.
- [ ] Add the exact `website` resource shown above. Do not add `WithHttpEndpoint`, a hard-coded port, an npm install command, a copied `../../website` path, or a readiness shim.
- [ ] Compile the AppHost to verify the exact current `AddViteApp` and `WithExternalHttpEndpoints` signatures:

  ```powershell
  dotnet build hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --no-restore
  ```

- [ ] Run the focused application-model test and the complete HostTests project:

  ```powershell
  dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj `
      --filter "FullyQualifiedName~ProductionTopology.WebsiteIsAnExternalViteResource" `
      --logger "console;verbosity=minimal"
  dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj `
      --logger "console;verbosity=minimal"
  ```

- [ ] Stage the slice, answer the three commit questions, and commit:

  ```powershell
  git commit -m "feat(apphost): host the VitePress website"
  ```

---

## Task 3: Prove the website lifecycle from a clean source copy

**Files:**

- No production file is expected to change.
- Modify only Task 2 files if the official integration reveals a real defect.

- [ ] Create a uniquely named directory under `[System.IO.Path]::GetTempPath()`. Export the committed source with `git archive HEAD`; do not copy `docs/node_modules`, `bin`, `obj`, `.codegraph`, or user secrets.
- [ ] Record the exact contents of root `aspire.config.json` before starting.
- [ ] From the isolated source copy, run:

  ```powershell
  aspire start `
      --apphost hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj `
      --isolated `
      --non-interactive `
      --format Json
  aspire wait website `
      --apphost hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj `
      --status up `
      --timeout 180 `
      --non-interactive
  aspire describe website `
      --apphost hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj `
      --format Json `
      --non-interactive
  aspire logs website `
      --apphost hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj `
      --tail 200 `
      --timestamps `
      --non-interactive
  ```

- [ ] From the JSON description, obtain the external HTTP URL and perform an HTTP GET. Require a 2xx response containing the DigitalBrain documentation page.
- [ ] Verify all of the following from state and logs:

  - `website` is up and its HTTP endpoint is healthy;
  - the URL is external and clickable;
  - Vite received Aspire's allocated port;
  - the official JavaScript lifecycle installed clean-clone dependencies;
  - no custom install process was needed;
  - the AppHost build refreshed CodeGraph.

- [ ] In a `finally` path, run:

  ```powershell
  aspire stop `
      --apphost hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj `
      --non-interactive
  aspire ps --format Json --non-interactive
  ```

  Confirm no AppHost from the isolated copy remains. Validate the resolved cleanup path starts with the resolved system temp root before recursively removing the isolated copy.
- [ ] Compare `aspire.config.json` to the recorded content. If the CLI changed it, restore the exact original content with `apply_patch` and verify no diff remains.
- [ ] If this proof is green without source changes, do not create an empty commit. If it exposes a real integration defect, add a focused failing proof, make the smallest correction, rerun the whole task, answer the commit questions, and commit the correction separately.

---

## Task 4: Make durable OAuth token writes atomic with Orleans state

**Files:**

- Modify: `tests/DigitalBrain.Simulations/CentralMcpContracts.cs`
- Modify: `src/DigitalBrain.Integrations.Mcp/DurableMcpTokenCache.cs`

- [ ] Add `FailedTokenCommitRestoresTheLastDurableValue`. Store a first token successfully, configure the next commit to throw, attempt to store a different token, then recreate the cache and assert the first token is still visible and the failed token is absent.
- [ ] Run the focused test and observe that the current cache leaks the staged failed value:

  ```powershell
  dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj `
      --filter "FullyQualifiedName~FailedTokenCommitRestoresTheLastDurableValue" `
      --logger "console;verbosity=minimal"
  ```

- [ ] In `StoreTokensAsync`, copy the prior byte array, stage the newly protected token container, await the supplied commit, and restore the prior array before rethrowing if the commit fails. Do not merge refresh tokens: the stable SDK cache contract lacks enough grant context to distinguish an omitted refresh token from a narrowed reauthorization.
- [ ] Run all `CentralMcpContracts`.
- [ ] Stage, answer the three commit questions, and commit:

  ```powershell
  git commit -m "fix(mcp): roll back failed durable token writes"
  ```

---

## Task 5: Replace the imitation MCP client layer with one concrete official-SDK connection

**Files:**

- Create: `src/DigitalBrain.Integrations.Mcp/McpConnection.cs`
- Modify: `src/DigitalBrain.Integrations.Mcp/McpOAuth.cs`
- Modify: `src/DigitalBrain.Integrations.Mcp/AssemblyInfo.cs`
- Delete: `src/DigitalBrain.Integrations.Mcp/McpClient.cs`
- Modify: `modules/DigitalBrain.Modules.Google/Gmail.cs`
- Modify: `modules/DigitalBrain.Modules.Salesforce/Salesforce.cs`
- Modify: `tests/DigitalBrain.Simulations/CentralMcpContracts.cs`
- Modify: `tests/DigitalBrain.Simulations/AccountEnrichmentCompositionContracts.cs`
- Modify: `tests/DigitalBrain.Simulations/McpTestDoubles.cs`

**Resulting internal surface:**

```csharp
internal sealed class McpRuntime
{
    internal McpConnection Connect(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity);
}

internal sealed class McpConnection
{
    internal ValueTask<IReadOnlyList<McpToolAdmission>> AdmitAsync(
        IReadOnlyList<McpToolPolicy> policies,
        CancellationToken cancellationToken);

    internal ValueTask<JsonElement> ExecuteAsync(
        McpToolPolicy policy,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);

    internal ValueTask<JsonElement> ExecuteAsync(
        McpToolAdmission admission,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}
```

- [ ] Rewrite the transport contract tests so they invoke the concrete `McpRuntime`/`McpConnection` against `FakeMcpHttpServer`. Keep the fake at the HTTP protocol boundary; do not fake `McpConnection`, `ModelContextProtocol.Client.McpClient`, or a replacement client interface.
- [ ] Add assertions that a normal read uses the official sequence `initialize`, `tools/list`, `tools/call`, disposes the client, propagates cancellation to HTTP, rejects protocol errors, and requires structured content.
- [ ] Run the focused contracts and observe compilation failures for the not-yet-created concrete API.
- [ ] Implement `McpRuntime.Connect` as the per-Neuron composition point for configuration, the durable token cache, payload protection, and authorization options.
- [ ] Implement each operation with the official stable SDK:

  ```csharp
  var transport = new HttpClientTransport(options, httpClient);
  await using var client = await ModelContextProtocol.Client.McpClient.CreateAsync(
      transport,
      cancellationToken: cancellationToken);
  var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
  var result = await selectedTool.CallAsync(
      arguments,
      cancellationToken: cancellationToken);
  ```

  Adjust only for signatures proven by the compiler. Let `McpClient.DisposeAsync` own the transport lifecycle; retain no extra `McpSession` wrapper.
- [ ] For a read, list/admit/call on one official connection. For a previously fenced mutation admission, open a fresh official connection, list again, compare the admission fingerprint, then call the newly listed `McpClientTool`.
- [ ] Replace provider constructor dependencies on `IMcpClientFactory` with concrete `McpRuntime`.
- [ ] Replace `RecordingMcpClientFactory`, `RecordingGmailClient`, and `RecordingSalesforceClient` with one configurable HTTP MCP handler. Provider behavior tests must traverse the same official protocol path as production.
- [ ] Delete `IMcpClientFactory`, `IMcpClient`, `SdkMcpClientFactory`, `SdkMcpClient`, and `McpSession`.
- [ ] Compile all direct consumers:

  ```powershell
  dotnet build src/DigitalBrain.Integrations.Mcp/DigitalBrain.Integrations.Mcp.csproj
  dotnet build modules/DigitalBrain.Modules.Google/DigitalBrain.Modules.Google.csproj
  dotnet build modules/DigitalBrain.Modules.Salesforce/DigitalBrain.Modules.Salesforce.csproj
  dotnet build hosts/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj
  ```

- [ ] Run `CentralMcpContracts` and `AccountEnrichmentCompositionContracts`.
- [ ] Stage, answer the three commit questions, and commit:

  ```powershell
  git commit -m "refactor(mcp): use one concrete official SDK connection"
  ```

---

## Task 6: Make OAuth private, fail-closed, optional-secret aware, and account-bound

**Files:**

- Modify: `src/DigitalBrain.Integrations.Mcp/McpOAuth.cs`
- Modify: `src/DigitalBrain.Integrations.Mcp/McpConnection.cs`
- Modify: `src/DigitalBrain.Integrations.Mcp.Aspire.Hosting/McpHosting.cs`
- Modify: `modules/DigitalBrain.Modules.Google/Gmail.cs`
- Modify: `modules/DigitalBrain.Modules.Salesforce/Salesforce.cs`
- Modify: `modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting/SalesforceHostingExtensions.cs`
- Modify: `tests/DigitalBrain.Simulations/CentralMcpContracts.cs`
- Modify: `tests/DigitalBrain.Tests/IntegrationHostingContracts.cs`

- [ ] Add failing contracts proving:

  - the default runtime refuses interactive authorization;
  - loopback mode accepts only HTTP loopback redirect URIs and the exact callback path;
  - the loopback redirect creates a cryptographically random state, adds it to the browser URL, and rejects a callback with a different state;
  - Google browser authorization adds `access_type=offline` and `login_hint=<Neuron name>`;
  - Salesforce can construct official `ClientOAuthOptions` with no client secret;
  - two Gmail Neurons with the same owner and different names use different protected token purposes and cannot read each other's token state;
  - `IMcpAuthorizationRedirect` is absent from the exported public surface.

- [ ] Run the focused tests and observe failures against the current public redirect interface, missing state generation, mandatory secret, and owner string that does not bind the full Neuron identity.
- [ ] Keep `ClientOAuthOptions`, `ITokenCache`, protected-resource discovery, authorization-server discovery, PKCE, bearer injection, token exchange, refresh, and 401/403 retry inside the official SDK. Do not recreate those behaviors.
- [ ] Make the development loopback delegate a private implementation detail selected by configuration. Delete public `IMcpAuthorizationRedirect`, `RejectingMcpAuthorizationRedirect`, and DI override support that has no authenticated-edge consumer.
- [ ] Generate state inside the loopback delegate before opening the browser and compare it in constant time on callback. Keep production fail-closed; do not invent a production callback host.
- [ ] Bind token protection to server key plus the full `NeuronId.ToString()` supplied by the provider, including the Gmail account name. Keep the shared mechanics package independent of DigitalBrain Neuron types.
- [ ] Model client-secret requirement in `McpServerDefinition`: Google requires its configured secret; Salesforce passes null when no secret is configured.
- [ ] Stop declaring/projecting a mandatory Salesforce client-secret parameter. Retain Salesforce client id and redirect URI.
- [ ] Do not rewrite Gmail protected-resource metadata, strip endpoint paths, preserve omitted refresh tokens, or adopt a preview SDK. The observed Google `/mcp` resource versus `/mcp/v1` endpoint mismatch remains a reported upstream live-auth blocker under stable 1.4.1.
- [ ] Run all MCP and hosting contracts:

  ```powershell
  dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj `
      --filter "FullyQualifiedName~CentralMcpContracts" `
      --logger "console;verbosity=minimal"
  dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj `
      --filter "FullyQualifiedName~IntegrationHostingContracts" `
      --logger "console;verbosity=minimal"
  ```

- [ ] Stage, answer the three commit questions, and commit:

  ```powershell
  git commit -m "refactor(mcp): bind private OAuth state to named neurons"
  ```

---

## Task 7: Strengthen exact tool admission without exposing MCP vocabulary

**Files:**

- Rename: `src/DigitalBrain.Integrations.Mcp/McpToolContract.cs` to `src/DigitalBrain.Integrations.Mcp/McpToolPolicy.cs`
- Modify: `src/DigitalBrain.Integrations.Mcp/McpConnection.cs`
- Modify: `modules/DigitalBrain.Modules.Google/Gmail.cs`
- Modify: `modules/DigitalBrain.Modules.Salesforce/Salesforce.cs`
- Modify: `tests/DigitalBrain.Simulations/CentralMcpContracts.cs`
- Modify: `tests/DigitalBrain.Simulations/McpTestDoubles.cs`

**Resulting policy concepts:**

```csharp
internal sealed record McpInputPolicy(
    string Name,
    string JsonType,
    bool Required,
    IReadOnlyList<string> AllowedStringValues);

internal sealed record McpToolAdmission(
    string Name,
    string Fingerprint);
```

- [ ] Add failing HTTP-boundary contracts for:

  - duplicate/missing tool names;
  - read-only/destructive annotation drift;
  - input-schema drift;
  - output-schema drift;
  - order-insensitive JSON object fingerprinting;
  - required versus optional input fields;
  - a `messageFormat` enum that must admit `FULL_CONTENT`;
  - rejection before `tools/call`.

- [ ] Run the focused tests and observe that the current fingerprint covers only input schema and the Gmail policy omits an argument it actually sends.
- [ ] Rename “contract” to “policy” because the type is provider-selected admission policy, not public domain vocabulary.
- [ ] Canonicalize and fingerprint only safety-relevant protocol fields: exact tool name, input schema, output schema, and behavior annotations. Do not fingerprint titles, descriptions, or prose.
- [ ] Gmail policy must require string `messageId`, require the schema to declare optional string `messageFormat`, and require `FULL_CONTENT` to be an allowed value; it remains read-only and non-destructive.
- [ ] Salesforce update policy must require `sobject-name`, `id`, and `body`; Salesforce reconciliation must require `query`. Keep these names internal.
- [ ] Re-list and compare immediately before every admitted call. Call the selected official `McpClientTool`; do not invoke by an unchecked string name.
- [ ] Run all central MCP and account-enrichment composition contracts.
- [ ] Stage, answer the three commit questions, and commit:

  ```powershell
  git commit -m "refactor(mcp): enforce exact safety-relevant tool policy"
  ```

---

## Task 8: Harden Salesforce fencing, cancellation, reconciliation, and rollback

**Files:**

- Modify: `modules/DigitalBrain.Modules.Salesforce/Salesforce.cs`
- Modify: `tests/DigitalBrain.Simulations/AccountEnrichmentCompositionContracts.cs`
- Modify: `tests/DigitalBrain.Simulations/McpTestDoubles.cs`

- [ ] Add failing public-Neuron proofs for:

  - cancellation before the durable `Invoking` fence performs no provider call;
  - cancellation or transport loss after the fence never retries the update;
  - post-fence reconciliation gets a bounded token rather than `CancellationToken.None`;
  - reconciliation opens a fresh connection, re-lists the query tool, and rejects query-schema drift;
  - inability to prove the provider value becomes durable `OutcomeUncertain`;
  - a failed Orleans state write restores the prior mutation ledger in memory;
  - terminal replay returns the original receipt and performs no MCP operation.

- [ ] Run each new test once and observe its intended failure before editing production.
- [ ] Preserve the existing correct order: exact approval evidence, admit update and query, persist evidence/fingerprints/`Invoking`, then contact Salesforce.
- [ ] Replace `CancellationToken.None` with a new bounded reconciliation `CancellationTokenSource`. Do not link it to an already-cancelled caller token after the fence; once the write may have happened, reconciliation is required even if the caller left. Bound it so a stuck provider cannot hold the grain forever.
- [ ] On any update exception after the fence, reconcile exactly once. A mismatch, timeout, cancellation, protocol error, auth error, or schema drift becomes `OutcomeUncertain`; none authorizes another update.
- [ ] Snapshot the previous serialized ledger entry around `WriteStateAsync` and restore it on failure.
- [ ] Keep proposal provider-free and keep Task vocabulary out of Salesforce.
- [ ] Run the complete composition contract class and direct Salesforce package build.
- [ ] Stage, answer the three commit questions, and commit:

  ```powershell
  git commit -m "fix(salesforce): harden fenced mutation recovery"
  ```

---

## Task 9: Give `Concurrent` and direct `GroupChat` one durable MAF session owner

**Files:**

- Create: `modules/DigitalBrain.Modules.AI/DirectAgentSession.cs`
- Modify: `modules/DigitalBrain.Modules.AI/Concurrent.cs`
- Modify: `modules/DigitalBrain.Modules.AI/GroupChat.cs`
- Delete: `modules/DigitalBrain.Modules.AI/OrchestrationState.cs`
- Delete: `modules/DigitalBrain.Modules.AI/SessionCompatibility.cs`
- Delete: `modules/DigitalBrain.Modules.AI/GroupChatWorkflow.cs`
- Modify: `tests/DigitalBrain.Simulations/AIOrchestrationContracts.cs`

**Resulting ownership:**

```text
Concurrent Neuron ─┐
                   ├─ DirectAgentSession ─ MAF AIAgent + one protected AgentSession
GroupChat direct ──┘

GroupChat supervised Attempt ─ WorkflowRunner + OrleansCheckpointStore
```

- [ ] Add a `Concurrent` proof mirroring the existing GroupChat proof: two turns on the same named Neuron must resume one protected MAF session, retain prior conversation, and store no plaintext prompt or response.
- [ ] Upgrade both direct-session proofs from deactivation-only to an actual silo-host restart with a shared journal provider. Assert activation changes, session history survives, and no second logical session is created.
- [ ] Add a failed journal-write proof: after MAF returns a response but `WriteStateAsync` fails, the staged session bytes roll back; retry restores the last committed session rather than the failed turn.
- [ ] Add drift proofs for both orchestration kinds. A participant, orchestration kind, MAF version, or manager-setting change must fail before participant calls and without mutating state.
- [ ] Run the new tests and observe that `Concurrent` creates and discards a fresh session every call and direct GroupChat leaks staged bytes on a failed write.
- [ ] Implement one internal `DirectAgentSession` that owns:

  - participant validation and MAF adaptation;
  - stable definition/fingerprint creation;
  - concrete `Concurrent` versus round-robin GroupChat workflow construction;
  - MAF `CreateSessionAsync`, `DeserializeSessionAsync`, `RunAsync`, and `SerializeSessionAsync`;
  - purpose-bound protect/unprotect;
  - compatibility rejection;
  - staged-state rollback around `WriteStateAsync`.

- [ ] Use one keyed durable value named `ai.direct-session` per orchestration Neuron. Fold the session envelope and compatibility code into `DirectAgentSession.cs`; delete the two shallow state/compatibility files instead of adding another layer beside them.
- [ ] Keep the public request/response as MEAI `ChatMessage`/`ChatResponse`; keep all MAF types internal.
- [ ] Fingerprint stable architectural inputs: orchestration type identity, orchestration kind, MAF assembly identity, execution environment, manager settings, and exact typed participant identities. Remove `ModuleVersionId`; a byte-identical rebuild is not an architecture change and must not invalidate every session.
- [ ] Bump the direct-session envelope format because orchestration kind and stable fingerprint inputs changed. Reject the old format with the existing explicit migration/reset failure; do not write a compatibility decoder for an undeployed PR artifact.
- [ ] Make `Concurrent.RespondAsync` and direct `GroupChat.RespondAsync` thin calls into this component.
- [ ] Inline the single round-robin builder decision into `DirectAgentSession` and the supervised runner, then delete the shallow `GroupChatWorkflow` wrapper.
- [ ] Keep direct GroupChat excluded while a supervised Attempt is active.
- [ ] Compile the AI module and run all `AIOrchestrationContracts`.
- [ ] Stage, answer the three commit questions, and commit:

  ```powershell
  git commit -m "refactor(ai): centralize durable direct MAF sessions"
  ```

---

## Task 10: Make supervised MAF workflow recovery reject drift and honor cancellation

**Files:**

- Modify: `modules/DigitalBrain.Modules.AI/GroupChat.cs`
- Modify: `modules/DigitalBrain.Modules.AI/WorkflowRunner.cs`
- Modify: `modules/DigitalBrain.Modules.AI/WorkflowRun.cs`
- Modify: `modules/DigitalBrain.Modules.AI/OrleansCheckpointStore.cs`
- Modify: `modules/DigitalBrain.Modules.AI/AIWorkerState.cs`
- Modify: `tests/DigitalBrain.Simulations/AIWorkerContracts.cs`

- [ ] Add failing public-worker proofs for:

  - a persisted checkpoint resumes after the silo hosting the worker is restarted;
  - a pending active run is redispatched after restart without duplicating an adopted superstep;
  - participant/manager/MAF version drift is rejected before checkpoint read or participant invocation;
  - cancellation stops the active runner and a late completion cannot be adopted;
  - a failed checkpoint-grain write leaves no readable or indexed ghost checkpoint;
  - protected checkpoints from one definition cannot be unprotected under another definition.

- [ ] Run each focused test and observe the current failures: deactivation is not a host restart, recovery trusts the stored definition, runner operations use `CancellationToken.None`, and checkpoint collections retain staged entries after a failed write.
- [ ] Before `ContinueAsync`, reminder recovery, dispatch, checkpoint adoption, and completion, describe the current concrete GroupChat definition and require it to match the persisted definition.
- [ ] Include the definition fingerprint in the checkpoint protection purpose while retaining the stable Worker/Task/Attempt checkpoint lineage.
- [ ] Add `[AlwaysInterleave] IWorkflowRunner.CancelAsync(Guid runId)` so cancellation can reach a runner whose long MAF turn is active. The runner owns one cancellation source for its active run and passes that token to MAF start/resume, turn-token send, stream watch, cancel, and disposal operations.
- [ ] Persist the worker's cancellation fence before signalling the runner. A late result remains rejected by the existing active-run match.
- [ ] Roll back `_payloads`, `_parents`, and `_order` if the checkpoint grain's `WriteStateAsync` fails.
- [ ] Do not move Tasks state or policy into AI. `GroupChat` remains the application-owned typed mapping from `Goal` to messages and messages to `Result`.
- [ ] Run all `AIWorkerContracts` and `AIOrchestrationContracts`.
- [ ] Stage, answer the three commit questions, and commit:

  ```powershell
  git commit -m "fix(ai): harden workflow restart and cancellation"
  ```

---

## Task 11: Make named Gmail selection and account-enrichment replay explicit

**Files:**

- Modify: `samples/DigitalBrain.AccountEnrichment/AccountEnrichmentFacts.cs`
- Modify: `samples/DigitalBrain.AccountEnrichment/AccountEnrichmentProcess.cs`
- Modify: `tests/DigitalBrain.Simulations/AccountEnrichmentCompositionContracts.cs`

**Public sample command:**

```csharp
public sealed record EnrichAccountFromEmail(
    CommandId CommandId,
    string GmailAccount,
    string MessageId,
    string AccountId) : Synapse;
```

- [ ] Add failing end-to-end proofs that:

  - `GmailAccount = "myemail@gmail.com"` resolves `NeuronId.For<IGmail>(owner, "myemail@gmail.com")`;
  - two account names resolve different Gmail Neurons and durable OAuth states;
  - replaying the same `CommandId` with identical input performs no additional Gmail read, Salesforce proposal, or semantic emission;
  - reusing the same `CommandId` with a different account, message, or Salesforce account id fails before provider calls;
  - replaying completed approval emits `AccountEnriched` only once.

- [ ] Run the new tests and observe that the current sample hard-codes `"gmail"`, performs provider calls before checking its ledger, and overwrites the same command entry.
- [ ] Add `GmailAccount` to the command. Validate it as a non-empty Neuron name and include it in the durable request fingerprint.
- [ ] Check the durable ledger before either provider call. Treat an exact replay as a no-op using the stored receipt/state; reject changed input.
- [ ] Persist enough state to distinguish proposed and completed commands and recognize their replays without repeating provider calls or emissions.
- [ ] Keep Salesforce proposal provider-free and approval as a separate delivered human-authority step.
- [ ] Do not add account registries, roles, permissions, tenant objects, or provider selectors. A named Neuron is the account selection mechanism for the current one-brain trust boundary.
- [ ] Run the complete account-enrichment composition class.
- [ ] Stage, answer the three commit questions, and commit:

  ```powershell
  git commit -m "fix(sample): make named account commands idempotent"
  ```

---

## Task 12: Delete remaining zero-value structure and pin package boundaries

**Files:**

- Delete: `hosts/DigitalBrain.AppHost/_probe_exec.cs.txt`
- Modify: `src/DigitalBrain.Integrations.Mcp/AssemblyInfo.cs`
- Modify: `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs`
- Modify: `tests/DigitalBrain.Tests/IntegrationContracts.cs`

**Required deletions by the end of this task:**

```text
IMcpClient
IMcpClientFactory
IMcpAuthorizationRedirect
SdkMcpClient
SdkMcpClientFactory
McpSession
GroupChatWorkflow
RecordingMcpClientFactory
RecordingGmailClient
RecordingSalesforceClient
hosts/DigitalBrain.AppHost/_probe_exec.cs.txt
```

- [ ] Use the refreshed CodeGraph MCP to inspect callers, callees, and project dependencies for every remaining type in:

  ```text
  src/DigitalBrain.Integrations.Mcp
  src/DigitalBrain.Security
  src/DigitalBrain.Aspire.Hosting
  modules/DigitalBrain.Modules.AI
  modules/DigitalBrain.Modules.Google
  modules/DigitalBrain.Modules.Salesforce
  samples/DigitalBrain.AccountEnrichment
  ```

  Confirm each result with `rg`; generated code and reflection-based module entry points count as consumers.
- [ ] Add failing package/reflection contracts asserting:

  - Google and Salesforce public assemblies expose semantic Neuron contracts only;
  - no public type exposes ModelContextProtocol or MAF types;
  - Tasks cannot reach AI, MAF, MCP, Google, or Salesforce;
  - the central MCP runtime cannot reach Google or Salesforce;
  - provider modules share the central runtime rather than referencing `ModelContextProtocol.Core` directly;
  - `hosts/DigitalBrain.Mcp` remains a northbound host and provider modules do not reference it.

- [ ] Run the focused boundary tests and observe any current violations before deleting code.
- [ ] Verify the required deletion list is gone, delete `_probe_exec.cs.txt`, and remove only the friend declarations made obsolete by those named deletions. Report any additional zero-consumer finding separately rather than expanding this slice without a behavioral proof. Do not delete a package merely because its types are internal; `DigitalBrain.Integrations.Mcp` is the intentional shared southbound mechanics boundary.
- [ ] Remove unused package/project references and friend declarations made obsolete by the deletions.
- [ ] Run:

  ```powershell
  dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj `
      --filter "FullyQualifiedName~PackageBoundaryContracts|FullyQualifiedName~IntegrationContracts" `
      --logger "console;verbosity=minimal"
  ```

- [ ] Compare aggregate source lines in the scoped runtime directories against the start of the task. Any net increase must be directly explained by a new behavioral guarantee; line count is a pressure, not a substitute for correctness.
- [ ] Stage, answer the three commit questions, and commit:

  ```powershell
  git commit -m "refactor: remove shallow runtime structure"
  ```

---

## Task 13: Record only the architecture decisions that actually changed

**Files:**

- Modify: `docs/architecture.md`

- [ ] Update the existing MCP section, not a new progress section:

  - the stable official SDK owns OAuth discovery, PKCE, bearer injection, refresh, and authenticated retry;
  - the central module adapts its token cache to Orleans, owns private local-development redirect mechanics, and enforces tool policy;
  - no public redirect/client interface exists without a production consumer;
  - named provider Neurons bind provider account identity and durable token state;
  - resource-identifier mismatches fail closed rather than being rewritten by provider-specific compatibility code.

- [ ] Update the AI section:

  - `Concurrent` and direct `GroupChat` share one internal durable session owner;
  - supervised GroupChat Attempts retain a separate checkpoint lifecycle;
  - compatibility fingerprints cover stable architecture, not build MVIDs;
  - host restart, write rollback, definition drift, and cancellation are required proofs.

- [ ] Update hosting:

  - the production AppHost owns external `website` from `../../docs` through official `AddViteApp`;
  - the testing AppHost remains internally scoped;
  - production versus local MCP authorization remains fail-closed versus explicit loopback.

- [ ] Do not add implementation history, file inventories, version-specific upstream incident prose, future roles, speculative integrations, or claims that live Gmail authorization passes.
- [ ] Run the docs checks:

  ```powershell
  Push-Location docs
  try {
      node tools/render-specification.mjs
      node --test tests/*.test.mjs
  }
  finally {
      Pop-Location
  }
  ```

- [ ] Inspect the generated-file diff. Commit only the architecture/specification changes required by the renderer.
- [ ] Stage, answer the three commit questions, and commit:

  ```powershell
  git commit -m "docs: record durable session and integration boundaries"
  ```

---

## Task 14: Run the unfiltered repository and live-runtime completion gate

**Files:**

- No source changes expected.
- Any discovered defect becomes a new red-green slice and separate commit before restarting this task.

- [ ] Re-record:

  ```powershell
  git rev-parse HEAD
  git status --porcelain
  git merge-base master HEAD
  git diff --stat master...HEAD
  git diff --check
  ```

- [ ] Run the exact website checks from `docs`:

  ```powershell
  Push-Location docs
  try {
      node tools/render-specification.mjs
      node --test tests/*.test.mjs
  }
  finally {
      Pop-Location
  }
  ```

- [ ] Run the exact unfiltered repository gate from the root:

  ```powershell
  dotnet test --logger "console;verbosity=minimal"
  ```

- [ ] Start the real production AppHost without `--no-build`:

  ```powershell
  aspire start `
      --apphost hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj `
      --isolated `
      --non-interactive `
      --format Json
  aspire wait website `
      --apphost hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj `
      --status up `
      --timeout 180 `
      --non-interactive
  aspire describe website `
      --apphost hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj `
      --format Json `
      --non-interactive
  aspire logs website `
      --apphost hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj `
      --tail 200 `
      --timestamps `
      --non-interactive
  node tools/codegraph.mjs status
  ```

- [ ] GET the described external website URL and require a 2xx DigitalBrain documentation response.
- [ ] Confirm the Aspire build log ran CodeGraph refresh and the MCP smoke handshake from Task 1 still returns a tool list.
- [ ] Stop the AppHost in a `finally` path:

  ```powershell
  aspire stop `
      --apphost hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj `
      --non-interactive
  aspire ps --format Json --non-interactive
  ```

- [ ] Check for leaked processes:

  ```powershell
  Get-Process |
      Where-Object ProcessName -Match 'Aspire|DigitalBrain.Host|DigitalBrain.ProbeHost|vite|node' |
      Select-Object Id, ProcessName, Path
  ```

  Terminate only processes whose command line or recorded id proves they belong to this verification run. Do not kill unrelated Node processes.
- [ ] Compare `aspire.config.json` with the pre-run content and restore it with `apply_patch` if the CLI changed it.
- [ ] Inspect every commit and the complete runtime diff against `master`. Verify the northbound `hosts/DigitalBrain.Mcp` changes are exactly the user-authored host plus unavoidable compiler adaptation, with no southbound provider mechanics added.
- [ ] Run the final checks:

  ```powershell
  git diff --check
  git status --porcelain
  git rev-parse HEAD
  git merge-base master HEAD
  ```

  Require an empty status.
- [ ] Report:

  - final HEAD and original planning ground;
  - commits by green slice;
  - exact gates run and their results;
  - source/types deleted;
  - the live website endpoint and observed health during the stopped run;
  - remaining scoped findings, including the stable Gmail resource-metadata blocker;
  - no claim that the architecture is perfect merely because the gates pass.
