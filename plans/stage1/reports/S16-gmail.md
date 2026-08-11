# S1.6 — Gmail strangler (GREEN) report

Report path: `plans/stage1/reports/S16-gmail.md`

## What changed

| File / area | Change |
|-------------|--------|
| `src/Modules/Google/Google/GoogleModule.cs` | Salesforce shape: registers `McpServerDefinition` + `ExternalServerCapability` for server key `google.gmail` (endpoint `https://gmailmcp.googleapis.com/mcp/v1`, scopes readonly+compose, `requiresClientSecret: true`). |
| `src/Modules/Google/Aspire.Hosting/GoogleHostingExtensions.cs` | OAuth hosting key/root aligned to `GoogleModule` constants; copy describes official Gmail MCP client. |
| `src/Modules/Google/Google/DigitalBrain.Modules.Google.csproj` | Dropped `Google.Apis.*` and AI package refs; description updated. |
| `src/Modules/Google/Contracts/**` | Emptied (Salesforce contracts shape); description notes live MCP catalog is the surface. |
| Typed path deleted | Removed `Gmail/`, `Auth/`, all `Contracts/Gmail/*` (`GmailAuthRail`, `Gmail` neuron, planner/provider/mapper, `DurableGoogleTokenStore`, `GoogleSignIn`, typed request/response contracts). |
| `Directory.Packages.props` | Removed `Google.Apis.Auth` and `Google.Apis.Gmail.v1` pins (removal granted; no additions). |
| `DigitalBrainComposition.cs` | Dropped `IGmail` contracts assembly from reflected contracts list; keeps `GoogleModule` implementation. |
| `McpAuthorizationRail.cs` | Google authorize URL defaults (`accounts.google.com/o/oauth2/v2/auth` + PKCE + offline/consent) for `google.gmail` — parallel to Salesforce defaults; generic rail remains sole mint. |
| `McpTokenExchange.cs` | Google token endpoint default `https://oauth2.googleapis.com/token` for `google.gmail`. |
| `BrainClusterFixture` + `FakeMcpTransport` | Registers Gmail server key; fake catalog returns Gmail-shaped tools for that key. |
| Flutter Behavior Studio demo fixtures | Typed `IGmail` / `GmailSearchRequest` → `IMcp` + `ListMcpTools` / `CallMcpTool` at `google.gmail`. |

**Server key note:** Brief example `google/gmail` is rejected by `IdentityPart` (grain owner/name separator is `/`). Production key remains `google.gmail` (valid neuron instance name `mcp:{owner}/google.gmail`).

## Tests

| Kind | Name | Assertion |
|------|------|-----------|
| Composition | `GoogleModuleRegistersGmailAsMcpServerDefinitionOnly` | Definition + capability only; no typed path |
| Composition | `GmailServerKeyIsAValidNeuronInstanceName` | `google.gmail` forms `mcp:dev/google.gmail` |
| Composition | `TypedGmailContractsAndAuthPathAreDeleted` | Gmail/Auth dirs gone; only `GoogleModule.cs` |
| Composition | `GoogleApisPackagePinsAreRemoved` | props + csproj free of Google.Apis |
| Composition | `EmptyGoogleContractsAssemblyReflectsNoGmailVocabulary` | No neuron types; composition has no IGmail |
| Composition | `ManifestReflectionFindsNoDeadGmailContracts` | MCP catalog only; no gmail.* facts |
| Rail (Theory) | `ProviderAuthorizeUrlAlwaysCarriesPkceS256` | salesforce **and** google.gmail host + PKCE S256 |
| Rail (Theory) | `McpTokenPurposesKeyByPrincipalNotNeuronIdentity` | principal purpose for both keys; not neuron/`google/oauth/` |
| Rail (Theory) | `CompletedAuthorizationCodeIsOneShotAndReplayIsRefused` | one-shot + replay refuse on both keys |
| Rail (Theory) | `AuthorizationPendingBindsTheLocalUserPrincipal` | Actor bound on pending for both keys |
| Rail (Theory) | `PrincipalTokenSlotsIsolateUserAFromUserB` | A/B isolation for both keys |
| Gateway | `GmailServerKeyListsTheFakeCatalog` | list-tools on `google.gmail` |
| Gateway | `GmailToolCallJournalsActorAndIntegrationSubject` | call-tool stamps actor + IntegrationSubject |

## In-process vs live-smoke

| Capability | In-process (this session) | Stage-1 exit live smoke |
|------------|---------------------------|-------------------------|
| Gmail as `McpServerDefinition` + `ExternalServerCapability` | **Proven** — module registration + composition proofs | Confirm AppHost projects OAuth client id/secret/redirect for `DigitalBrain:Google:Gmail` |
| Server key / grain identity `mcp:{owner}/google.gmail` | **Proven** — NeuronId validation + gateway list/call | Operator fires `db.mcp.list-tools` at that instance |
| S1.3 OAuth rail: PKCE sole mint, Begin/Claim/Deliver one-shot, replay refuse | **Proven** — Theory suite on `google.gmail` | Real Google authorize URL must complete browser consent |
| Principal-keyed Integration tokens (user A ≠ user B) | **Proven** — purpose isolation + cross-principal Begin refuse | Two real Google principals must not share tokens |
| Tool call journal audit (Actor + IntegrationSubject) | **Proven** — FakeMcpTransport call path | Live call should journal same stamps after sign-in |
| Live MCP catalog content | **Not proven** — FakeMcpTransport only | Sign-in against real Google; list real Developer Preview tools from `gmailmcp.googleapis.com` |
| Real token exchange + MCP tool execution | **Not proven** — no network; no live Google | Exchange code via Google token endpoint; call a read tool (e.g. search) successfully |
| Typed Gmail path absence | **Proven** — deletion proofs + package pin removal | N/A (gone) |

## Gate

```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(node NO_COLOR AppHost noise only — not C# warnings)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 165, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 179.906s
```

(Earlier full runs saw single known ticketed flakes under parallel pressure: DurableTurn restart / OutcomeUncertain — clean on re-run; not Gmail-related.)

## Conflicts & risks

- **Key shape:** Brief's `google/gmail` example cannot be a grain instance name (`IdentityPart` bans `/`). Used `google.gmail` (pre-existing GmailAuthRail key) so OAuth hosting parameter prefix `google-*` stays continuous.
- **Rail Google defaults:** Minimal provider defaults for authorize/token hosts (same pattern as Salesforce hardcodes). No second GmailAuthRail; no typed neurons. Generic `AuthorizeEndpoint`/`TokenEndpoint` config still overrides for other keys.
- **Endpoint reachability:** Default MCP endpoint is the official Developer Preview URI in the definition for production composition. Tests never open it (`FakeMcpTransport` / loopback definition). Live connectivity is Stage-1 smoke only.
- **Delete-and-reauthorize:** Pre-production; neuron-keyed Google token blobs are not migrated (typed store deleted). Users re-sign-in through the generic MCP rail.
- **Flutter demos:** Behavior Studio fixtures still offline; program source now references MCP verbs rather than deleted contracts.

## Out of scope

- Live Google OAuth / real Gmail MCP round-trip (headless cannot).
- Other Google Workspace MCP products (Drive, Calendar, …).
- Chat turn pipeline / find_capabilities UX polish beyond ExternalServerCapability registration.
- Aspire/AppHost run; git; package additions.
