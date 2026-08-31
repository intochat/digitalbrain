# Hosted Gmail MCP implementation

> Use subagent-driven-development to execute this plan on the existing branch. The user's explicit no-tests/current-branch instructions override skill defaults. No further design approval is needed.

## Authority and goal

Spec: user request at `C:/Users/vhorb/.codex/attachments/30e7b036-12f3-41f7-933b-3da9799b726a/pasted-text.txt`. Latest user instruction defers Google enrollment, secret inspection, private parameters, and live authenticated verification; implement the code now. The original request remains binding otherwise.

Extend the existing Integrations Gmail and shared MCP flow with kernel-owned OIDC, five carefully bounded assistant tools, read continuation, trusted draft approval, and provider-allowlisted UI. Keep the Salesforce experience intact.

## Global constraints

- Work in `D:/digitalbrain` on `codex/behavior-runtime`; no worktree or branch switch. Do not add or run tests. Use builds, static review, and live application checks where credentials permit.
- Preserve working Salesforce, OpenAI-only configuration, and the function-tool reasoning-effort fix. No AI-to-Integrations dependency or resurrected OAuth rail/modules.
- Mailbox traffic uses only `https://gmailmcp.googleapis.com/mcp/v1`, bearer on initialization and every request, no redirects or REST fallback. Fakes require explicit mode.
- Secrets and tokens stay kernel-private and volatile. Never expose them or OAuth codes in chat, journals, DTOs, logs, trace attributes, or non-OAuth URLs. Disable automatic AI content/tool capture. Email contents are untrusted data.
- Reuse generic UserActionRequest, IUserActionSource, IUserActionContinuation, and existing chat/MCP waiting infrastructure. Resume original reads once, never writes; expired/cancelled actions settle.
- Expose only gmail_get_current_account, gmail_search_threads, gmail_get_thread, gmail_list_labels, gmail_create_draft. Draft creation first previews exact recipients/subject/body and requires trusted user confirmation bound to preview, owner, chat/actor, connected account, and content. Model flags and login consent are not mutation approval. No automatic mutation retry.
- No real draft, cloud enrollment, credential changes, push, merge, or external publication in this code-only pass. Commit scoped code after verification. Clearly distinguish public protocol discovery from authenticated mailbox evidence.

## Verified hosted protocol (2026-08-31)

Public POST tools/list at the real endpoint returned HTTP 200. No credential or mailbox request was sent. Four allowed remote names are present; no get-profile tool exists.

| Remote name | Arguments used | Shape |
|---|---|---|
| search_threads | query, pageSize, pageToken, includeTrash, view | threads, nextPageToken, resultCountEstimate (string) |
| get_thread | threadId, messageFormat | id, messages |
| list_labels | empty object | labels (labelId/name) |
| create_draft | to, cc, bcc (plain email arrays), subject, body | id, messageId, threadId plus content to discard |

Search view THREAD_VIEW_MINIMAL includes subjects/snippets; THREAD_VIEW_METADATA_ONLY omits subjects. Hosted maximum pageSize is 50; app maximum is 10, default 3. get_thread defaults FULL_CONTENT remotely, so explicitly send MINIMAL unless the caller requests bodies, then PLAIN_TEXT (present in actual catalog). Do not expose RAW, HTML, attachments, or reply-to mutation semantics.

Sources:
- https://developers.google.com/workspace/gmail/api/guides/configure-mcp-server
- https://developers.google.com/workspace/gmail/api/reference/mcp
- https://developers.google.com/workspace/gmail/api/reference/mcp/tools_list/search_threads
- https://developers.google.com/workspace/gmail/api/reference/mcp/tools_list/get_thread
- https://developers.google.com/workspace/gmail/api/reference/mcp/tools_list/create_draft
- https://developers.google.com/workspace/guides/configure-mcp-security
- https://developers.google.com/identity/openid-connect/openid-connect
- https://developers.google.com/identity/branding-guidelines

## Task 1: Kernel authentication, tools, and trusted execution

Implement the complete backend Gmail slice. Read the Global constraints and Verified hosted protocol sections in this plan as well as this task. Read the original spec, but enrollment/runtime-private actions are deferred by the latest user instruction. Do not add or run tests, do not spawn subagents, stay on the current branch. Use apply_patch for edits.

### Files and boundaries

- Extend `src/Modules/Integrations/Gmail`, `Mcp/{IMcpIntegrationClient,McpIntegrationClient,McpIntegrationEndpoint}.cs`, and `IntegrationsModule.cs`.
- Add focused Gmail files for OIDC configuration/handler/endpoints, volatile connections and pending actions/completion worker, bearer handler, bounded content screening, tool source, and draft-preview store. Separate responsibilities; use existing Salesforce conventions without blindly duplicating protocol details.
- Add the supported `Microsoft.AspNetCore.Authentication.OpenIdConnect` dependency matching the current ASP.NET preview family in Directory.Packages.props and Integrations csproj. Inspect its APIs using dotnet-inspect skill, restore/build to verify version.
- Wire AddGmailBrowserAuthorization/UseGmailBrowserAuthorization in Kernel Program. Both Gmail and Salesforce pre-auth path guards must precede one UseAuthentication invocation; minimally adjust the Salesforce extension if needed.
- Add a generic trusted user-command handler contract under Abstractions/Interactions, registered by Integrations and invoked by UI ChatTurnWorker before calling the assistant, under verified actor + AgentTurnContext. UI must not reference Integrations.
- Add minimal generic Chat.cs lifecycle hooks: cancel pending provider actions on authenticated turn cancellation; publish the exact immutable preview as the authoritative answer and mark it actionable only after that answer is persisted. An arbitrary LLM answer does not count as displaying the preview.
- Update Assistant instructions with real capabilities/confirmation syntax and evidence requirements. Disable sensitive tool/message telemetry in AI pipeline irrespective of development config for this application. Do not alter OpenAI reasoning logic.
- Adjust ChatTurnWorker context assembly so external email/context payloads do not become System instructions; preserve trusted system metadata separately from screened untrusted data.

### OAuth and volatile ownership

Configuration keys under DigitalBrain:Integrations:Gmail:OAuth: ClientId, ClientSecret, PublicOrigin (same kernel origin, localhost:5080 in dev). Fixed callback /integrations/gmail/callback; login /integrations/gmail/login?request=<opaque random id>. Missing config fails closed with sanitized setup guidance rather than secret values.

Use OpenIdConnectHandler code flow + UsePkce, S256, validated state/correlation and nonce, issuer/audience/lifetime/signature validation. Google authority accounts.google.com. Exact public-origin checks, GET-only callback/login, HttpOnly correlation cookie, no redirect leakage, no auth cookie or tokens in tickets/journals. Validated Google `sub`, verified `email`, and granted token scopes establish stored account; verify granted readonly and openid/email claims/scopes as applicable. Reject mismatched/incomplete scope or identity; compose incremental uses include_granted_scopes=true, access_type=offline and consent when refresh is needed. Initial scopes openid email https://www.googleapis.com/auth/gmail.readonly; compose only when draft requested.

Pending action state is random, expiring, bounded, one-use and tied to owner/chat/actor/command. Reuse UserActionRequest provider gmail and existing continuation. A background worker completes success/denial/expiry; cancellation or stale callback cannot resume. Read-only names are the four read tools; draft-auth actions have empty ResumeToolNames. Do not generate repeated login actions during the same continuation. Store tokens in memory by owner+validated Google sub, with connection revision; require reconnect after restart. Tokens never serialized. Refresh single-flight with no redirects; revoked/invalid refresh clears connection; transient failures are sanitized and remain operational errors, not endless consent loops.

### Transport and mapping

Retain IMcpIntegrationClient callers compatibly while adding explicit OwnerId for Gmail calls. GmailSearchHandler currently drops owner: fix it and every fake/not-implemented implementation without conflating the existing `account` sender filter with connection identity. Only the owner chooses credentials.

Validate Gmail endpoint exactly. Use separate Gmail session cache or provider-aware key including owner+Google sub+connection revision; never share with Salesforce. Per-session synchronization, bounded lifetime and count, cancellation/timeouts. Authentication is attached to initialization and each HTTP request by a handler that validates the target URI; disallow redirects. Invalidate sessions on account/revision changes. Only four allowed remote names; validate tools/list presence and the argument schema's field/enum support against the actual catalog. No hosted get-profile call.

Bound each operation at 30 seconds, overall response bytes at 1 MiB, returned JSON at 32 KiB, search pageSize 1..10/default3 and no automatic pagination (pageToken <=2048); query <=2048, thread ids <=256; truncate/project messages to maximum 10 and plain text body <=12000 characters, with explicit truncation markers. Exclude raw/html/attachments. List labels maximum100 with explicit truncation; do not conceal meaningful HTTP or protocol errors. At most one automatic token refresh/read retry; zero mutation retries, even uncertain network outcome.

Current-account tool combines stored validated identity plus a successful list_labels/read MCP call; never claim current connectivity from local identity alone. Tool result errors sanitized without service response text, exception messages carrying email, or OAuth payloads.

### Draft preview and trusted confirmation

gmail_create_draft accepts to/cc/bcc/subject/body only, validates plain emails and bounds (total recipients <=20, subject <=998, body <=12000), and returns a complete preview, opaque random preview id, expiry and exact user instruction `confirm gmail draft <id>`. It never performs a write. Bound preview store (max128, 10 minutes) to owner/chat/actor/current Google sub+revision+exact immutable payload. Do not accept model confirmation flags.

The generic trusted command handler sees only original authenticated user text, before any model processing, and recognizes the exact confirmation command. It loads stored content, validates bindings and expiry/connection, then atomically consumes the preview before any create_draft call. Result caches by preview/command prevent duplicate creates; an uncertain network result is permanently non-retryable and instructs checking Drafts. Missing compose triggers a non-resuming login action; consent cannot itself create a draft. Require a fresh preview/confirmation after auth/reconnect if account/revision changed. Return only created id/result metadata; discard echoed content. Never run trusted confirmation on a resumed auth turn or on transcript/model/tool text. First preview must be visible before confirmation is actionable; do not expose a way for the model to manufacture trusted user input.

### Screening and privacy

Implement own documented prompt-injection defense, not just a System warning. Normalize and deterministically reject role/control/prompt-injection patterns; independently classify bounded outgoing Gmail args and incoming projected email/label/draft content using a tool-less OpenAI request with fixed security instruction and fail-closed JSON decision. Do not allow classifier tools or sensitive telemetry. Mark returned data untrusted, not authority; screen before SmartPrompt/context insertion too. Fail-closed classifier outage must give sanitized actionable error, never allow arbitrary content through. Defense is best-effort and must be described with residual risk, not claimed complete isolation.

Verify with `dotnet build src/Hosts/Kernel/DigitalBrain.Kernel.csproj --no-restore` if path exists (discover actual kernel csproj), or restore/build selected kernel project as necessary. No tests. Static inspect all paths, ensure no mailbox network calls without user credentials. Self-review, commit only owned changes, full report at the assigned report path with build commands/results, security assumptions and remaining live checks.

## Task 2: Aspire, privacy defaults, Gmail card, and setup guide

Implement the configuration/UI/documentation slice after Task1. Read Global constraints and Verified hosted protocol in this plan as well as this task. User explicitly forbids adding or running tests. Use apply_patch; stay on current branch; no subagents.

### Aspire and telemetry

- `src/Aspire/DigitalBrain.AppHost/AppHost.cs`: required gmail-client-id and secret gmail-client-secret, descriptions link https://developers.google.com/workspace/gmail/api/guides/configure-mcp-server. Inject only kernel env DigitalBrain__Integrations__Gmail__OAuth__ClientId/ClientSecret, DigitalBrain__Integrations__Gmail__Mcp__Endpoint=https://gmailmcp.googleapis.com/mcp/v1 and PublicOrigin using the existing kernel public origin. Callback http://localhost:5080/integrations/gmail/callback.
- Remove development default forcing fake Gmail and fake mode. Retain fake transport/resource only behind explicitly selected DigitalBrain:Fakes:Enabled true/1 (Testing mode counts explicit); ensure real endpoint never appears to accept a fake endpoint. Preserve Salesforce/OpenAI-only config. Keep Gmail secrets out of DigitalBrain shared module projection, MCP, UI and traces. Use the existing DigitalBrainNames.Fakes key; direct FakeGmailTransport may remove the need for a fake MCP resource.
- Set AI EnableSensitiveData false, OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=false and the ASP.NET/HttpClient disable-query-redaction flags false for kernel/MCP as appropriate, to override Aspire defaults. ServiceDefaults HTTP enrichment must not hide all 404/409 errors; scope any expected Azure noise handling to known Azure resources, or remove broad suppression. Review log filters for OAuth to avoid payload/exception/token leakage while preserving status and sanitized errors.

### Flutter

- Generalize `shell/lib/user_actions/chat_login_action.dart` explicit provider-to-backend-route allowlist: salesforce -> /integrations/salesforce/login; gmail -> /integrations/gmail/login. Require same backend origin, https or loopback http, no userinfo/fragment, exactly one nonempty request query param. Never trust arbitrary model URLs. Preserve prior Salesforce API surface where useful for existing callers, without changing tests.
- Reuse common login-card behavior (expiry, opening external browser, cancel turn) with a new Google/Gmail-branded card or generalized provider card. Use official Google G asset/button branding (Google guideline link above), exact button label `Sign in with Google`, white/light button background and approved icon. No raster recreation. Update brain_chat_screen provider dispatch and pubspec assets as required. No new OAuth helper page.
- The controller downloaded Google's official https://developers.google.com/static/identity/images/signin-assets.zip into this plan's ignored scratch workspace. Use the intact pre-approved light pill PNG from `Android + Web/PNG @2x/Light/Theme=Light, Show text=Yes, Shape=Pill, Platform=Android+Web@2x.png` (already extracted as `google-signin-light-2x.png` in that workspace). It includes the current gradient G and approved text; image size 360x80, display 180x40 preserving ratio, accessible semantics and keyboard/click activation. This avoids unsupported SVG text/font rendering. Record provenance in the committed asset README. Binary asset copy from download is allowed; authored code edits still apply_patch.
- Ordinary draft preview is textual recipients/subject/body with exact trusted confirmation command from Task1; no model-generated button may bypass confirmation.

### Durable documentation

Create `docs/integrations/gmail-mcp.md` describing official preview enrollment, Workspace account eligibility, gmail.googleapis.com + gmailmcp.googleapis.com, web OAuth client, exact callback, consent screen Branding/Audience/Data Access/test users, readonly initial and compose incremental scopes, offline/refresh, Workspace admin allowlisting/service controls. Do not claim enrollment or existing secrets verified.

Document volatile kernel-private token storage/restart reconnect, owner+account isolation, prompt-injection screening/residual risk, no message/tool telemetry, drafts preview confirmation and uncertain-write at-most-once behavior. Include actual protocol catalog date/fields, bounds, private Aspire parameter entry instructions (no secrets in chat), exact original live read prompt and unconfirmed-draft check, separate MCP/UI principals, trace inspection and stop command. State authenticated live checks remain deferred until user provides params and consent.

Build AppHost/kernel/MCP and `flutter analyze` or `dart analyze` affected packages (analysis is not running tests); if feasible `flutter build windows --debug` for compile evidence. Do not run tests. Self-review and commit owned changes, full report with commands/results and remaining runtime limitations.

## Verification and handoff (controller)

Review each committed task against spec and quality; fixes return to its implementer. Run whole-range review. Build selected AppHost/kernel/MCP and Flutter using supported tooling, no tests. Inspect privacy configuration, middleware ordering, fake default, draft path, and git diff. If credentials are unavailable, report exact missing configuration and defer authenticated runtime; do not invent trace URLs or claim live Gmail success. Stop any started Aspire instance, include commit hash, concise changes and live-test boundary in final.
