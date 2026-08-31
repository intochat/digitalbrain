# Salesforce login and resume implementation plan

> Execute the approved conversation design in the current checkout. Use the subagent-driven-development skill for independent UI/chat work and review. User constraints override skill defaults: do not add or run tests, do not create another branch/worktree.

**Goal:** Asking for a Salesforce profile offers login when disconnected, authorizes in the external browser, and resumes the same request through hosted MCP.

**Architecture:** Integrations owns OAuth, volatile credentials, pending authorization and authenticated Salesforce transport. Generic action contracts connect the chat worker, Flutter and MCP edge; AI has no reference to Integrations. OAuth uses the framework authorization-code handler with S256 and explicit trusted Salesforce endpoints because SDK 2.2 discovery rejects Salesforce's missing PKCE advertisement.

**Tech stack:** .NET 11, ASP.NET Core OAuth, Orleans, MCP SDK 2.2.0, Flutter, Aspire.

**Spec:** Approved design in this conversation, August 31, 2026.

## Constraints

- Current branch; no tests or test changes. Verify by builds and live DigitalBrain MCP.
- Only hosted Salesforce MCP for data; preserve tool names, shapes, Gmail, OpenAI configuration and reasoning fix.
- No deleted SalesforceModule or old authorization rail. No delete tools. OAuth consent never approves writes.
- Tokens and client secret never enter chat, journals, DTOs, telemetry or browser storage. Credentials remain volatile and reconnect after kernel restart.
- Pending requests bind trusted owner, actor, chat and command; expire; validate browser correlation and state. Callback consumes state once.

## Tasks and contracts

- [x] Integrations OAuth: consumer-key/secret Aspire parameters; stable kernel callback; OAuth handler and secret-free errors; owner-scoped token cache and serialized refresh; Salesforce-only bearer handler; login-needed classification. Initializers and tools use the same transport.
- [x] Generic chat actions: context propagated through Orleans RequestContext; pending-action provider queried after the AI attempt; WaitingForUser turn releases queue; completion requeues the same command once. Resumed agent tools restricted to the trusted read-only allowlist. Cancel and restart do not silently execute a mutation.
- [x] Flutter: consume optional userAction in chat-turn events and render branded login/cancel card inline. Browser launch is user initiated and platform safe. Existing chat events remain compatible.
- [x] MCP: return URL-mode elicitation when supported; safe structured fallback otherwise. Retry of same command returns eventual response, never duplicates original work or replays a stale login action.
- [ ] Verify: build affected projects and Flutter; review security and full diff; start Aspire, supply configuration privately, invoke send_chat_message, complete browser consent, invoke same command to obtain real profile. Inspect api.salesforce.com trace, sanitized logs and exception status. Stop Aspire, commit and report exact trace URL and limitations.

## Decisions

- User approved implementation; no additional design approval is needed.
- Persist pending non-secret metadata in chat, but keep OAuth transaction and tokens in memory. An interrupted login after kernel restart requires a new login action.
- Read-only resumption is enforced in code, not by assistant instructions alone.

## Local setup

1. In the existing Salesforce External Client App, register `http://localhost:5080/integrations/salesforce/callback`. Enable PKCE, JWT access tokens for named users, and Require Secret for Web Server Flow. Allow `mcp_api` and `refresh_token` scopes. See [Salesforce's setup guide](https://developer.salesforce.com/docs/platform/hosted-mcp-servers/guide/create-external-client-app.html).
2. Start with `aspire start --apphost src\Aspire\DigitalBrain.AppHost\DigitalBrain.AppHost.csproj`. Enter `salesforce-consumer-key` and `salesforce-consumer-secret` privately in the Aspire parameter dialog. Keep the required OpenAI secret configured. Do not paste credentials into chat.
3. Ask DigitalBrain to get your Salesforce profile. Use its Salesforce **Log in** action and authorize in the external browser. The existing read request resumes automatically. An MCP caller retrieves its result by repeating `send_chat_message` with the same text, command ID and chat name.
4. Restarting the kernel clears Salesforce access and refresh tokens; log in again. A pending login expires after ten minutes. OAuth consent never confirms a Salesforce mutation.

## Verification record

- Kernel and DigitalBrain MCP builds succeeded with zero warnings or errors. Flutter analysis, web build and Windows release build succeeded. No tests were added or run.
- Security and end-to-end code reviews covered OAuth state/correlation, one-use callbacks, durable continuation delivery, read-only resumption, cancellation, trusted login URLs and MCP capability negotiation.
- Aspire started successfully. The live `send_chat_message` attempt for "Use Salesforce to tell me which Salesforce user is authenticated." failed with `Initialization timed out`: `salesforce-consumer-key` and `salesforce-consumer-secret` were still `ValueMissing`, leaving kernel and MCP in `Waiting`.
- Real Salesforce authorization, the successful profile response, and the required successful `api.salesforce.com` trace remain unverified. No successful trace URL is available yet. Complete these checks after the user supplies credentials and browser consent; stop Aspire afterward.
- Aspire was stopped successfully after the blocked verification attempt. The next start will request the two missing Salesforce parameters again.
