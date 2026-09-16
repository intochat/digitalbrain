# Identity and public hosting

## Module boundary

`IdentityModule` is built into the DigitalBrain runtime. Like other modules, it configures both the silo and its HTTP endpoints. The host installs routing, listener isolation, authentication, caller context, and authorization once.

Identity owns accounts, external identity links, opaque sessions, one-use link codes, workspace memberships, and automation grants. They are ordinary Orleans state, not neurons and not available to behavior composition or graph inspection. A single non-reentrant directory grain makes linking and code consumption atomic. It uses the existing durable grain storage; session tokens and link codes are stored only as hashes. Expired records are pruned on writes.

Telegram contributes `IExternalIdentityVerifier`: it validates signed Mini App `initData`, including its age, and returns the numeric Telegram user ID as its immutable subject. The identity module decides whether that subject can sign in. An unlinked Telegram account cannot sign in or become the installation owner.

## Bootstrap and link an account

1. Configure the existing private host credentials `DigitalBrain:Auth:Username` and `DigitalBrain:Auth:Password`.
2. On the **private listener**, POST `/identity/owner/bootstrap` with those HTTP Basic credentials. No configured credentials means no bootstrap. The public listener rejects this route.
3. Use the returned session as a bearer token to POST `/identity/link`. It returns a one-use code valid for five minutes.
4. From the Mini App, POST `/identity/link/redeem/telegram` with JSON `{ "credential": "<raw Telegram.WebApp.initData>", "code": "<one-use code>" }`. The verified Telegram identity is linked to the issuing account and receives its own session. The owner's session token never goes into Telegram.
5. Subsequent login: POST `/identity/login/telegram` with `{ "credential": "<raw initData>" }`.

Sessions last 12 hours. The response includes the token, CSRF token and expiry, and sets `__Host-DigitalBrainSession` (Secure, HttpOnly, SameSite=Strict). Browser cookie mutations require `X-CSRF-Token`; bearer authentication does not. GET `/identity/session` reads the current session; POST `/identity/logout` revokes it. Revocation and disabled-account checks read durable state, so restarts do not resurrect access. Clients must not log or place credentials in URLs.

Telegram now builds the shared Flutter shell through `shell/lib/main_telegram.dart` and opens the Projects homepage after authentication. Unlinked accounts see a code-entry screen. In the desktop shell, Settings → Connections → Link Telegram mints the code using configured owner credentials. Existing per-Telegram reminder APIs retain their signed proof protocol.

## Public endpoint policy

`ModuleEndpointMetadata` identifies ownership only. Public exposure additionally requires `PublicModuleEndpointMetadata` and an explicit `AllowAnonymous` or `RequireAuthorization` policy. Telegram's restricted listener allows Telegram, Identity and explicitly mapped UI workspace endpoints. Workspace, agent, table and brain observation APIs require an authenticated installation-owner session on that listener. Unmatched paths, graph mutation/MCP, health and other private routes remain hidden. Telegram webhook and reminder endpoints still verify their own provider proofs.

Existing unscoped host APIs accept application sessions only for the installation owner. Private Basic/local-development access remains compatible. Workspace membership is durable but **does not yet partition existing neurons or make global APIs safe for multiple users**.

Only the authenticated user ID crosses into Orleans request context. A grain filter checks live owner/account status for session-originated neuron calls. The Orleans cluster remains private and trusted; request context is not a substitute for securing a cluster gateway. No raw session token is propagated to grains. Durable reactions do not depend on a browser session surviving.

## Behavior grants

POST `/identity/grants` with an authenticated session:

```json
{
  "workspaceId": "owner",
  "automationId": "behavior:my-behavior",
  "targets": ["chart:my-chart"],
  "actions": ["<interface-alias>.<method-alias>"]
}
```

Use the actual `NeuronId.ToString()` values and descriptor aliases. The response contains `grantId`. Each listed action is allowed on each listed target; matching is exact, with no wildcards. Only a current workspace owner may issue a grant.

Add `Authorization = new BehaviorAuthorization(workspaceId, grantId)` to the behavior definition. Session-originated Save/Start requires that the grant belongs to the caller and matches this behavior. Processor activations persist the grant and behavior ID and cannot replace them within a run. Every action dispatch checks the grant, current account status, workspace ownership, target and action. DELETE `/identity/grants/{grantId}` prevents subsequent dispatch; it cannot cancel an already admitted external effect. A denied new action pauses with diagnostics and retains its input, without falsely marking it as an uncertain side effect. Previous attempts remain subject to reconciliation.

Existing trusted server-created behaviors with no authorization field remain compatible, including Telegram's built-in reminder behavior. This is a migration boundary: these built-ins are not implicitly converted into user-delegated automations. Fine-grained source-read permissions and tenant-scoped neuron ownership remain separate work.

## Named Cloudflare tunnel

Configure the AppHost (non-secret settings):

```json
{
  "Telegram": {
    "TunnelMode": "Named",
    "PublicUrl": "https://brain.example.com",
    "PublicPort": 5181
  }
}
```

Create a remotely managed tunnel in Cloudflare and configure its published hostname to service `http://localhost:5181` on the machine running the AppHost. Supply its **tunnel token** through the Aspire secret parameter `cloudflare-tunnel-token` (`Parameters:cloudflare-tunnel-token` in AppHost user secrets or the deployment secret store). The connector receives `TUNNEL_TOKEN`; credentials are not command-line arguments. The bot token remains the separate `telegram-bot-token` secret.

Named mode uses the fixed, unproxied public listener and does not scrape temporary tunnel URLs. Missing/invalid origin or port fails configuration. A broad Cloudflare API key is unnecessary to run an already provisioned tunnel. This change does not provision Cloudflare DNS/tunnels or include a production deployment recipe.

`Quick` retains the existing development tunnel. `External` uses an already hosted HTTPS origin without launching a connector. For backward compatibility, omitted mode chooses External when PublicUrl exists, otherwise Quick. Explicit Named mode never falls back to Quick.

## Validation

Tests cover durable session/link/grant state, cold restarts, account disablement, one-use link redemption races, forged/expired Telegram proofs, cookie CSRF, invalid credential fallback, public listener isolation, revoked behavior dispatch after restart, grant ownership and exact scope, and stable named-tunnel configuration. No live named tunnel can be validated until its real hostname and token are configured.

Identity foundation validation on 2026-09-16: full Release suite 603 passed, 6 skipped; after the serialization/bootstrap safeguard, all 56 focused identity, endpoint and Telegram tests passed. Shared-shell follow-up: 69 backend tests, 63 Flutter core/shell tests, clean Flutter analysis, and successful Telegram-target web build.

Projects currently use device-local preferences; sharing the same shell does not synchronize a desktop project's local layout/history into Telegram.

Live follow-up verification: restarted AppHost with durable storage retained, waited for a healthy kernel, and opened the served `/telegram/app/` bundle in Chrome. It displays the shared shell's Telegram sign-in entry screen. An actual Telegram owner sign-in was not automated; the shared Projects screen after login is covered by Flutter widget tests. Clients requesting bearer-only session transport avoid setting a session cookie, so linking does not change the desktop's existing Basic login.
