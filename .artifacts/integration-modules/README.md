# refactor/integration-modules

Branch built on `master` @ `baf95c34` (your current local master). Two commits:

1. `ac44f4d8` refactor(modules): split Integrations into Google and Salesforce on a shared SDK
2. `aef51555` refactor(sdk): one browser login rail for Gmail and Salesforce

## Apply

From `D:\digitalbrain` (clean working tree):

```powershell
git fetch .artifacts\integration-modules\integration-modules.bundle refactor/integration-modules:refactor/integration-modules
git checkout refactor/integration-modules
```

Alternative, if you prefer patches: `git am .artifacts\integration-modules\*.patch` on a new branch.

## Layout after the change

```
src/Kernel/DigitalBrain.Sdk/            DigitalBrain.Sdk (ns DigitalBrain.Sdk)
  Mcp/    McpEndpoint, McpToolClient<TConnection>, IMcpCredentials<TConnection>,
          McpToolPolicy, McpSessionOptions, BearerTokenHandler, Mcp*Exception
  OAuth/  BrowserLogins, BrowserLoginDefinition, BrowserLoginSurface,
          BrowserLoginCorrelation, BrowserLoginWorker<TLogins>, LoginPage
  Http/   IHttpSurface, UseModuleHttpSurfaces()
src/Modules/Google/{Contracts,Google,Aspire.Hosting}        IGmail, GoogleModule, WithGmail()
src/Modules/Salesforce/{Contracts,Salesforce,Aspire.Hosting} ISalesforce, SalesforceModule, WithHostedMcp()
src/Modules/SmartPrompt/SmartPrompt/Search/                 IWebSearch + WebSearchHandler (moved)
```

Deleted: `src/Modules/Integrations`, `src/Testing/DigitalBrain.Integrations.Fakes`,
`tests/DigitalBrain.E2E.Tests/FakeIntegrationMcpTests.cs`.

AppHost now reads:

```csharp
.AddModule<GoogleModule>(google => google.WithGmail())
.AddModule<SalesforceModule>(salesforce => salesforce.WithHostedMcp())
```

Kernel `Program.cs` no longer names a provider: `builder.Services.AddAuthentication();`
and `app.UseModuleHttpSurfaces();` (before `UseAuthentication`/`UseBasicAuthGate`).

## Names that changed / stayed

| | before | after |
|---|---|---|
| Aspire parameters | `gmail-client-id`, `gmail-client-secret`, `salesforce-consumer-key`, `salesforce-consumer-secret` | unchanged (your user-secrets still apply) |
| Login URLs | `/integrations/gmail/...`, `/integrations/salesforce/...` | unchanged (registered callbacks, Flutter allowlist) |
| Gmail config | `DigitalBrain:Integrations:Gmail:OAuth:*`, `...:Gmail:Mcp:Endpoint` | `DigitalBrain:Google:Gmail:OAuth:{ClientId,ClientSecret,PublicOrigin}`; endpoint is a module constant |
| Salesforce config | `DigitalBrain:Integrations:Salesforce:OAuth:*`, `...:Salesforce:Mcp:Endpoint` | `DigitalBrain:Salesforce:OAuth:{ConsumerKey,ConsumerSecret,PublicOrigin}`, `DigitalBrain:Salesforce:Mcp:Endpoint` |
| PublicOrigin | constant `http://localhost:5080` in AppHost | derived from the kernel's own `http` endpoint; `WithGmail(publicOrigin:)` / `WithHostedMcp(publicOrigin:)` override |
| Transport seams | `IGmailTransport`, `ISalesforceTransport`, `IWebSearchTransport` | `IGmail`, `ISalesforce`, `IWebSearch` (owner-less overloads removed) |
| Exceptions | `Gmail*Exception`, `SalesforceAuthenticationRequiredException` | `McpAuthenticationRequiredException`, `McpOperationException` |

## Behavior differences worth knowing

- Fakes mode (`DigitalBrain:Fakes:Enabled` or Testing) now selects the in-process
  `FakeSalesforce` as well; before, the AppHost still pointed fakes mode at the real
  hosted Salesforce endpoint. Neither module declares Aspire parameters in fakes mode.
- Salesforce logins now use the Gmail rail semantics: one-use begin/claim, the
  `IsWaitingAsync` check before completing the turn (closes the "callback finished before
  the login card was published" race), cancellation registration, expired logins are
  delivered as denied, and a resumed turn cannot mint a second login.
- Both MCP sessions run with `EnableStandaloneGetStream = false` and no auto-reconnect
  (previously Gmail only). Salesforce lists the catalog once per session instead of on
  every call.
- Login result pages are HTML for both providers (Gmail's were text/plain).

## Verified here (Linux, .NET SDK 11.0.100-preview.7.26381.103)

- `dotnet build DigitalBrain.slnx`: 0 warnings, 0 errors (TreatWarningsAsErrors, preview-all analyzers).
- `dotnet format --verify-no-changes` clean on every touched file.
- `DigitalBrain.Aspire.Tests`: 51/55. The 4 failures pre-date the branch (AppHost enables
  OpenAI only; tests expect Google/Anthropic/XAI keys and an Ollama embedding).
- `DigitalBrain.Simulation.Tests`: 74/77. The 3 failures are the same SmartPrompt
  compiler/example tests that fail on master. 4 new `BrowserLoginsTests` pass.
- MCP SDK 2.2.0 confirmed (source + a probe) to surface 401 as `HttpRequestException`
  with `StatusCode`, which the read-only retry in `McpToolClient` relies on.

## Not verified here — please check live

- Gmail sign-in and Salesforce login end to end through `aspire start` (OAuth needs the
  browser and your private parameters). Watch for the login card, the callback page, and
  that the interrupted read resumes once.
- E2E tests (`DigitalBrain.E2E.Tests`) need Docker/Azurite and were not run.
