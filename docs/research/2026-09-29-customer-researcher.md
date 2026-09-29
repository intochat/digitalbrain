# Customer Researcher: existing capabilities, gaps, and proposed structure

Research date: 2026-09-29. Scope: repository inspection and official documentation; no application was launched and no browser integration was implemented or tested. Recommendations below are design conclusions, not verified runtime behavior. The user clarified that the browser must appear inside Flutter and visibly show the agent researching at runtime. The current [IntoChat AppHost](../../src/Applications/IntoChat/AppHost/AppHost.cs) calls `RunDesktopApp()` and also contains a web-host branch. This supports a desktop-first recommendation for the present Windows workspace, without treating Windows-only support as an explicit user requirement.

## Finding

The requested visible browser does not exist yet. `IWebBrowser` is a state contract and the Flutter `UiWebBrowser` is a title/URL placeholder. The current AI browser launches a separate headless Chromium session. Wiring their URLs together would display neither the actual browser nor the same interactive session.

For the current desktop launch path on Windows, recommend a real embedded WebView2 plus Playwright CDP attachment: it is a documented route to controlling the same visible browser. The component must show live page content, not a mirrored URL or a second browser loading the same address. For Flutter web, a streamed view of the agent's server browser is the practical general-purpose alternative; a normal cross-origin iframe cannot provide arbitrary company-site embedding and DOM automation. A stream meets the visible-same-session experience, but is not a local native webview.

## Repository evidence

| Area | Observed implementation |
| --- | --- |
| Browser contract | [`IWebBrowser.cs`](../../src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Contracts/WebBrowser/IWebBrowser.cs) exposes `Navigate` and `Read`; state contains name, version, URI, title. It has no browser-session identity, target handle, DOM actions, screenshot, or connection lifecycle. |
| Browser grain | [`WebBrowserNeuron.cs`](../../src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter/WebBrowser/WebBrowserNeuron.cs) validates an HTTP(S) URI and persists it with a navigation signal. It does not own a browser engine. |
| Actual rendering | [`ui_web_browser.dart`](../../src/Modules/Google/Flutter/app/ui/lib/src/components/browser/ui_web_browser.dart) builds a decorated box with two `Text` widgets. It contains no WebView, iframe, or platform view. The [UI package manifest](../../src/Modules/Google/Flutter/app/ui/pubspec.yaml) contains no webview dependency. |
| Existing checks | [`ui_kit_widgets_test.dart`](../../src/Modules/Google/Flutter/app/ui/test/ui_kit_widgets_test.dart) explicitly tests that browser title and URI appear. [`ComponentRenderingWebFacts.cs`](../../src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Tests.E2E/Rendering/ComponentRenderingWebFacts.cs) checks title visibility; it does not prove external page rendering. |
| Agent browser | [`PlaywrightBrowserSession.cs`](../../src/Modules/AI/DigitalBrain.Modules.AI/Web/PlaywrightBrowserSession.cs) calls `Chromium.LaunchAsync` with `Headless = true`, tries bundled Chromium then Edge/Chrome channels, and creates a new context and page. There is no connection to `IWebBrowser`. |
| Agent tools | [`PlaywrightWebAgent.cs`](../../src/Modules/AI/DigitalBrain.Modules.AI/Web/PlaywrightWebAgent.cs) creates and disposes a session for each run, exposes search/navigation/snapshot/follow-link tools, and validates company address/email against visited first-party pages. It already supplies useful evidence checking and ambiguity handling. |
| Package | [`Directory.Packages.props`](../../Directory.Packages.props) pins Microsoft.Playwright 1.62.0. The [AI project](../../src/Modules/AI/DigitalBrain.Modules.AI/DigitalBrain.Modules.AI.csproj) references it directly. |

The external inspiration files inspected were `E:/projects/IAW/src/Agents/Web/IPlaywright.cs` and `PlaywrightAgent.cs`. They supply an agent contract and MCP-backed browser tools, but launch `npx -y @playwright/mcp@latest --headless`. They therefore do not demonstrate embedded-browser control. Copying that launch strategy would retain the session mismatch.

## Windows desktop: same WebView2 session

Playwright's official .NET WebView2 guide documents enabling `--remote-debugging-port` on the WebView2 environment and connecting with `playwright.Chromium.ConnectOverCDPAsync(...)`. Its example retrieves the existing browser context and page. Thus an embedded WebView2 can remain the rendering owner while agent tools use that page. [Playwright WebView2 guide](https://playwright.dev/dotnet/docs/webview2).

CDP attachment supports Chromium browsers and has lower fidelity than Playwright's native protocol; advanced behavior needs verification on the actual WebView2 runtime. [ConnectOverCDPAsync reference](https://playwright.dev/dotnet/docs/api/class-browsertype#browser-type-connect-over-cdp).

Recommended design, contingent on Windows:

1. Implement a genuine WebView2-backed Flutter browser component and native host bridge. Selecting a Flutter plugin versus a custom Windows host integration remains separate work; no plugin has been validated here.
2. Create the dedicated browser instance/profile and CDP endpoint before reporting the component ready. Associate that instance with an explicit workspace/session identifier.
3. Attach agent tools to the existing target. Do not call `NewContextAsync` or `NewPageAsync` for the displayed research page. Resolve the correct target by session identity rather than assuming `Pages[0]` when multiple views exist.
4. Keep browser ownership distinct from agent ownership: stopping research should detach/cancel agent work according to the UI lifecycle rather than applying the existing session's blanket disposal behavior to a user-visible host.
5. Keep CDP local to a trusted desktop bridge, scoped to the workspace and browser session. Do not put a raw debugging endpoint into durable UI state. Confirm where the Orleans/backend process runs: its `localhost` resolves to its own host or container, not automatically the desktop. A remote/containerized backend needs a desktop-side automation service or authenticated bridge; a colocated process still needs deliberate session registration and access control.

These are architecture recommendations inferred from the attachment mechanism and repository lifetime mismatch, not capabilities already present in Flutter.

## Flutter web: iframe and streaming alternatives

Flutter's `HtmlElementView` can embed HTML in its web widget tree. That is a rendering facility, not a browser-control endpoint. [Flutter API](https://api.flutter.dev/flutter/widgets/HtmlElementView-class.html).

Cross-origin documents cannot freely inspect one another's DOM. Moreover, a destination can refuse embedding through CSP `frame-ancestors`. These are independent obstacles: even a website that permits framing does not grant its parent DOM access. [HTML origin model](https://html.spec.whatwg.org/multipage/browsers.html#origins), [CSP frame-ancestors specification](https://w3c.github.io/webappsec-csp/#directive-frame-ancestors).

Playwright attached externally to a deliberately managed browser can automate its frames, but an ordinary deployed Flutter web application does not acquire such an attachment merely by adding an iframe. Requiring every user's client browser to expose debugging would be a different deployment model. Therefore an iframe alone is not a sound general-purpose Customer Researcher browser design.

Recommended web design: the backend owns one browser context/page per research session, and the Flutter browser area displays frames from that exact page. Agent actions and any supported user input target that same page. Chromium's CDP includes experimental `Page.startScreencast` and frame events/acknowledgements; these are transport primitives, not a complete Flutter viewer. [CDP Page domain](https://chromedevtools.github.io/devtools-protocol/tot/Page/).

A production stream would require frame delivery, resizing, session authorization, disconnect handling and, if interactive, input coordinate mapping and arbitration between user and agent. It would show arbitrary sites as top-level content in the server browser rather than framing those sites directly. This is a proposed architecture; no streaming layer was found or validated here. If the user requires a literal locally embedded browser engine, choose Windows desktop instead.

## Reuse and gaps

Preserve the current agent's evidence provenance and null/ambiguous results. Its extraction currently covers website, address and email; new fields such as a structured location require schema and verification rules. A location must distinguish registered address, office, and headquarters when the source does.

Do not transplant the current browser policy unchanged into a visual browser. Its route handler blocks images/media/fonts, non-GET/HEAD requests and WebSockets, removes cookies, and fulfills responses through a restricted HTTP transport. Those choices support limited public text research but can impair visual rendering and interaction. The policy should be deliberately reconciled with the selected visible-browser implementation. Source: [`PlaywrightBrowserSession.cs`](../../src/Modules/AI/DigitalBrain.Modules.AI/Web/PlaywrightBrowserSession.cs).

An eventual browser abstraction should represent a live session, attach/readiness state and the page used by every tool, while `IWebBrowser` continues to describe the UI surface. A proposed Microsoft/Playwright module can own automation adapters and contracts, avoiding a dependency on the customer-specific workflow. This separation is a recommendation, not an existing module capability.

Before calling implementation complete, demonstrate that an agent navigation changes the visible page, a visible interaction changes the next agent observation, and two simultaneous sessions never target each other's page. Also verify cancellation and reconnect behavior on the chosen platform. Existing title/URL tests cannot establish any of these properties.

## Documentation method

Context7 resolution selected `/websites/playwright_dev_dotnet` for .NET API coverage and retrieved the official WebView2/CDP examples. Flutter was resolved to `/websites/api_flutter_dev`; the focused query returned no result, so the official Flutter API page was read directly. Remaining browser constraints were checked against the HTML/CSP specifications and Chromium's protocol documentation. No claims here depend on third-party tutorials.

## Application and composition findings

The desired two-row window fits the existing application model. An application implements
`IApplication`, declares module requirements, composes a UI surface and activates its application
neuron. The Assistant is the concrete working reference, rather than the older manifest-only apps.
Sources: [IApplication](../../src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps/Applications/IApplication.cs),
[AssistantApp](../../src/Apps/Assistant/AssistantApp.cs).

The shell already has an Applications menu and a generic `_openApplication` HTTP launch path.
However, the current application endpoint registration explicitly registers Assistant, and its
start/open routes are Assistant-specific. Customer Researcher needs an entry, application registration
and workspace-scoped open endpoint; creating a project alone will not make it appear in the menu.
Sources: [launcher entries](../../src/Modules/Google/Flutter/app/shell/lib/workspace/app_launcher.dart),
[menu](../../src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_islands.dart),
[launch path](../../src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_app.dart),
[application endpoints](../../src/Applications/IntoChat/IntoChat/Applications/ApplicationEndpoints.cs),
[Assistant window creation](../../src/Apps/Assistant/AssistantPresentation.cs).

Browser composition requires additional wiring beyond replacing the standalone widget:

- `UiComposer` has text fields, buttons, surfaces and layouts, but no browser helper.
- `CompositionValidation.Children` currently rejects `webbrowser` children.
- `RendererRegistry` lists `webbrowser` as a known kind but excludes it from dedicated renderers.
- The app node-read endpoint has no browser case. It must load the browser state under the same
  workspace ownership checks as the other UI nodes.

Sources: [UiComposer](../../src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter/Composition/UiComposer.cs),
[composition validation](../../src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter/Shared/CompositionValidation.cs),
[renderer registry](../../src/Modules/Google/Flutter/app/ui/lib/src/composition/renderer_registry.dart),
[app endpoints](../../src/Applications/IntoChat/IntoChat/Apps/AppEndpoints.cs).

## Agent reuse

The AI module already registers `browse_web` and `lookup_company`. `PlaywrightWebAgent` supports
model selection, search, navigation, text observations, visited sources and evidence validation.
Its current tools are limited to search/navigation/snapshot/following links; it does not currently
offer the IAW example's general click/fill/evaluate tool set. Its evidence-checked company result
currently covers website, address and email, not arbitrary requested fields.
Sources: [AIModule registration](../../src/Modules/AI/DigitalBrain.Modules.AI/AIModule.cs),
[PlaywrightWebAgent](../../src/Modules/AI/DigitalBrain.Modules.AI/Web/PlaywrightWebAgent.cs).

The reusable tool extension point is `IAgentToolFactory`; `AgentDefinition.Tools` selects tools for
an agent. Reuse that mechanism for session-bound browser actions. Keep the company-specific prompt,
field schema and save operation in the Customer Researcher application. Moving browser mechanics
out of the AI module must account for the existing native `browse_web`/`lookup_company` consumers;
do not remove those registrations without a compatible replacement.
Sources: [agent tools](../../src/Modules/AI/DigitalBrain.Modules.AI.Contracts/Agents/AgentTools.cs),
[agent definition](../../src/Modules/AI/DigitalBrain.Modules.AI.Contracts/Agents/AgentDefinition.cs),
[turn runner](../../src/Modules/AI/DigitalBrain.Modules.AI/Agents/AgentTurnRunner.cs).

Recommendation: use the existing pinned Microsoft.Playwright .NET package for the first implementation.
The IAW MCP agent is useful inspiration for tool names and behavior, but adds a Node/MCP process and
launches its own headless browser. Reusing that launch command would not satisfy the visible-session
requirement. This is a design recommendation, not a limitation of every possible Playwright MCP setup.

## PostgreSQL persistence gap

The new Postgres module and Aspire hosting project provide database provisioning, named connection
configuration, health checks, read-only queries and schema discovery. They are suitable infrastructure,
but the IntoChat AppHost currently registers Supabase and ClickHouse rather than `PostgresModule`.
Customer Researcher will need an AppHost/module reference and managed or external Postgres configuration.
Sources: [Postgres hosting](../../src/Modules/Postgres/DigitalBrain.Modules.Postgres.Aspire.Hosting/PostgresModuleHosting.cs),
[IntoChat AppHost](../../src/Applications/IntoChat/AppHost/AppHost.cs).

`IPostgres` has only `Query`, `ReadSchema` and `ReadConnection`. Its provider executes every statement
in a read-only transaction, so saving research through `Query` cannot work. Also, the module's keyed
data-source identifier is currently internal: there is no supported public application write seam.
Sources: [IPostgres](../../src/Modules/Postgres/DigitalBrain.Modules.Postgres.Contracts/IPostgres.cs),
[provider](../../src/Modules/Postgres/DigitalBrain.Modules.Postgres/PostgresProvider.cs),
[runtime hosting](../../src/Modules/Postgres/DigitalBrain.Modules.Postgres/PostgresHosting.cs).

Recommended first write path: an app-owned typed `CompanyResearchStore` using parameterized Npgsql
commands against the configured Postgres database. Add a deliberate public data-source registration
or factory seam in the Postgres module so the store shares its pool without hard-coding an internal
key. Keep `IPostgres.Query` read-only. An alternative is a general parameterized write contract in
Postgres, but that expands the module's public API and is unnecessary for one app-owned schema.
These are proposed changes, not existing APIs.

Proposed initial table: `company_research`, with `research_id`, `workspace_id`, `requested_name`,
`company_name`, `website`, `city`, `country`, `address`, `email`, `phone`, `industry`, `summary`,
`status`, `requested_fields`/`extra_fields` JSONB, `evidence` JSONB and `researched_at`. Start with
website and location plus a small agreed field set. Each field should retain its source URL and
evidence; missing facts stay null. Location should identify whether it describes a headquarters,
registered address or another office when known.

Use a stable research ID for retry-idempotent saves; do not merge companies merely because their
names match. Workspace identity comes from the authenticated app scope, not model output. The app
owns schema migration, validation and transaction boundaries. Only show "Saved" after the database
commit succeeds. For ambiguous company names, request a website/location clarification in the input
row instead of inserting an asserted identity. No outbound messages are part of this workflow.

## Proposed structure

Keep root-level neuron/contract files, following the requested Postgres/Time style rather than a
generic `Query` folder:

```text
src/Modules/Microsoft/Playwright/
  DigitalBrain.Modules.Microsoft.Playwright.Contracts/
    IPlaywright.cs
    BrowserSession.cs
    BrowserObservation.cs
    BrowserAction.cs
  DigitalBrain.Modules.Microsoft.Playwright/
    PlaywrightModule.cs
    PlaywrightHosting.cs
    PlaywrightNeuron.cs
    PlaywrightTools.cs
    BrowserSessionRegistry.cs
    Configuration/PlaywrightModuleConfiguration.cs
  DigitalBrain.Modules.Microsoft.Playwright.Tests.Unit/

src/Apps/CustomerResearcher/
  DigitalBrain.Apps.CustomerResearcher.csproj
  CustomerResearcherApp.cs
  ICustomerResearcher.cs
  CustomerResearcherNeuron.cs
  CompanyResearch.cs
  CompanyResearchStore.cs
  instructions.md
  Tests/
```

This is a candidate layout, not a scaffold. Add a Playwright Aspire.Hosting project if the chosen
desktop bridge or remote-browser service needs its own Aspire resource; it should not launch an
unrelated hidden browser merely to match another module's project count. Windows embedding/bridge
code belongs alongside the Flutter Windows host. No specific plugin or bridge implementation has
yet been proven suitable.

Proposed `IPlaywright` is a generic session-oriented browser neuron: attach to an owned browser
session, navigate, observe, click, fill and detach/cancel. The existing generic `IAgent` performs
reasoning and calls those tools. This adapts the IAW idea to this repository's separation of tools
and agents, rather than putting company research into every browser session. Use fully qualified
names or an SDK alias where the new contract collides with `Microsoft.Playwright.IPlaywright`.

`IWebBrowser` remains the Flutter-facing surface contract, extended with an opaque session identity
and browser-ready/navigation/status events. Its live engine and Playwright tools bind to the same
session. Transient browser handles, connection tokens and CDP endpoints do not belong in persisted
grain state. Browser recreation after a disconnect must not silently resume a stale research action.

## User flow and implementation order

The window has exactly two main rows:

1. A compact company-name input row with Research/Stop and inline progress, clarification or save status.
2. An expanding `IWebBrowser` surface showing the live page the agent is controlling.

On submit: create a research run, wait for the visible browser to be ready, bind the agent's tools
to that session, find the company, visit relevant pages, validate requested fields and sources,
then save a typed row in PostgreSQL. Retain the final page for inspection. Stop/window-close must
cancel further browser actions and suppress a late save for a cancelled run. Two windows must have
independent browser sessions, and a rerun must not mix observations from an older company.

Suggested delivery order:

1. Prove a real Flutter desktop WebView2 can be observed and navigated through Playwright CDP on the
   same visible page. Include resize, two windows and disconnect behavior. This is the first technical
   uncertainty to resolve, before investing in the full application.
2. Add the Microsoft/Playwright module and the Flutter browser composition/bridge support.
3. Compose Customer Researcher from the existing application, input, layout and agent infrastructure.
4. Add the typed Postgres write seam, app-owned schema and retry-safe saves.
5. Register the menu/open route and AppHost resources; exercise the complete visible workflow.

Acceptance checks: a menu launch opens the two-row window; typing a company visibly drives the same
browser the tools observe; sourced website/location data reaches PostgreSQL; unsupported fields remain
null; ambiguity is shown rather than guessed; two windows stay isolated; cancel and failed saves are
reported accurately. The current research establishes the code gaps and documented attachment path,
not a tested end-to-end solution.
