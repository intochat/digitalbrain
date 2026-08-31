# Hosted Gmail MCP setup and safety boundary

DigitalBrain connects to the hosted Gmail MCP service at
`https://gmailmcp.googleapis.com/mcp/v1`. It does not use Gmail REST mailbox
fallbacks. This guide describes the required setup; it does **not** assert that
a project was enrolled, that APIs or admin controls were verified, or that
credentials exist.

## Before entering credentials

1. Request the official [Gmail API preview](https://developers.google.com/workspace/preview)
   with an eligible Google Workspace account and project details. Preview
   enrollment is separate from OAuth, can take days, and does not approve a
   public customer release.
2. In the same Google Cloud project, enable both `gmail.googleapis.com` and
   `gmailmcp.googleapis.com`.
3. Create a **web application** OAuth client. Its only local redirect URI for
   this application is exactly
   `http://localhost:5080/integrations/gmail/callback`.
4. Finish the consent screen's Branding, Audience (including test users while
   the app is in testing), and Data Access configuration. Start with the
   readonly scope `https://www.googleapis.com/auth/gmail.readonly`; compose is
   requested incrementally only when a user asks to prepare a draft:
   `https://www.googleapis.com/auth/gmail.compose`. The identity scopes are
   `openid` and `email`/`userinfo.email`.
5. Keep offline access enabled so the kernel can refresh the connection while
   it is running. Tokens are not persisted: a kernel restart requires reconnect.

For Google's current MCP-specific instructions, see the [official hosted Gmail
MCP guide](https://developers.google.com/workspace/gmail/api/guides/configure-mcp-server).
For hosted-tool request and response screening guidance, see [MCP security
guidance](https://developers.google.com/workspace/guides/configure-mcp-security).

Workspace administrators can independently restrict an OAuth client or Gmail
scopes. Ask the administrator to assess this exact client ID and these scopes
under **Security > Access and data control > API controls**; do not disable
service controls globally. Google's [API controls guidance](https://knowledge.workspace.google.com/admin/apps/control-which-apps-access-google-workspace-data)
describes the relevant allowlisting decision.

## Private Aspire parameters

Start the AppHost locally:

```powershell
aspire start --apphost src\Aspire\DigitalBrain.AppHost\DigitalBrain.AppHost.csproj
```

When prompted by the dashboard, enter `gmail-client-id` and
`gmail-client-secret` privately. Aspire can save them to the AppHost's existing
user-secrets store; do not paste them into chat, source control, or appsettings.
They are injected only into the kernel as
`DigitalBrain__Integrations__Gmail__OAuth__ClientId` and
`DigitalBrain__Integrations__Gmail__OAuth__ClientSecret`. The AppHost also
injects the fixed hosted endpoint, the exact callback's public origin
`http://localhost:5080`, and no Gmail secrets into the shared DigitalBrain
module, MCP resource, UI, or trace configuration.

`DigitalBrain:Fakes:Enabled=true` (or `1`) and `DigitalBrain:Mode=Testing` are
the only explicit fake selections. Development mode alone does not select a
fake Gmail endpoint or transport. In real mode the endpoint is fixed to the
hosted Google service above.

## User flow and isolation

The browser begins at the kernel's same-origin login endpoint, then Google
returns with OIDC code + PKCE S256, nonce, state, and correlation validation to
the exact callback. Local callback cookies use `SameSite=Lax` with
`response_mode=query`. The Flutter client accepts login links only for the same
backend origin and the explicit `/integrations/gmail/login` or
`/integrations/salesforce/login` route with one nonempty `request` query value.
It never opens a model-provided arbitrary URL. The Gmail card uses Google's
official light `Sign in with Google` asset at 180 x 40.

OAuth access and refresh tokens live only in kernel memory and are discarded on
restart. A Gmail connection is isolated by DigitalBrain owner and the validated
Google account; Gmail and Salesforce connection/session caches are separate.
MCP and the visible UI use separate principals, so validate each one rather
than inferring that one connection proves the other.

Email content is untrusted data. DigitalBrain screens both prompt-side and
response-side content using a deterministic guard and a tool-less OpenAI
classifier, failing closed when screening cannot complete. This reduces but
does not eliminate prompt-injection risk, and the classifier creates additional
OpenAI processing of the content it screens. Neither message content nor tool
content is captured in AI/OpenTelemetry instrumentation. OAuth diagnostics are
sanitized and query redaction remains enabled; status/error evidence can still
be inspected without exposing token or code values.

Draft creation is deliberately two steps. The assistant can show an immutable,
textual preview of the exact recipients, subject, and body, but it cannot write
a draft. Only Task 1's trusted confirmation command, bound to the preview,
owner, chat/actor, connected account, and revision, can create it. A login or
model-generated button never bypasses that command. The preview is consumed
before the request and uncertain writes are not retried, giving at-most-once
behavior with a visible uncertain result when the remote outcome cannot be
known. The command is exactly `confirm gmail draft <id>` when the user chooses
to confirm; the unconfirmed check below intentionally does not issue it.

## Hosted protocol catalog

This catalog was publicly observed on **2026-08-31** with `tools/list` HTTP
200. No credentialed or mailbox request was sent. There is no `get-profile`
tool.

| Remote tool | Request fields used | Returned shape / DigitalBrain bounds |
| --- | --- | --- |
| `search_threads` | `query`, `pageSize`, `pageToken`, `includeTrash`, `view` | `threads`, `nextPageToken`, `resultCountEstimate` (string); app default 3 and maximum 10, while hosted maximum is 50. `THREAD_VIEW_MINIMAL` contains subjects/snippets and `THREAD_VIEW_METADATA_ONLY` omits subjects. |
| `get_thread` | `threadId`, `messageFormat` | `id`, `messages`; DigitalBrain explicitly requests `MINIMAL` by default or `PLAIN_TEXT` when bodies are requested, never RAW, HTML, attachments, or reply-to mutation semantics. |
| `list_labels` | empty object | `labels` with `labelId` and `name`. |
| `create_draft` | plain-email arrays `to`, `cc`, `bcc`, plus `subject`, `body` | `id`, `messageId`, `threadId`; returned content is discarded. |

## Deferred authenticated live pass

Authenticated checks remain deferred until the user provides the parameters
privately and completes the Google consent flow. Do not manufacture account,
trace, or enrollment evidence before then.

1. Verify preview enrollment, both APIs, consent-screen/test-user state, and
   the workspace administrator's client/scope policy for the selected account.
2. In the visible UI, use the original read request: **"Search my Gmail for the
   latest email from [sender] and show the subject only."** Verify the returned
   account only after validated OIDC plus a successful read.
3. In a separate MCP principal/session, repeat an allowed read. Check that its
   owner and account isolation are independently correct.
4. Request an **unconfirmed** draft preview and verify it displays only its
   textual recipients, subject, and body and creates no remote draft (including
   no new item in Gmail Drafts). Do not issue the exact trusted command
   `confirm gmail draft <id>` in this validation pass.
5. Inspect actual HTTP success/error trace URLs and attributes for status only:
   no mail/tool content, OAuth codes, access tokens, refresh tokens, or secrets.
   Record the real trace URLs produced by the run, then stop Aspire with the
   dashboard stop control (or the terminating `aspire` process).
