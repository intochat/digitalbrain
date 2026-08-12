# Seam 4 — B3 silent-fallback + B2 protect confirm

**Tip:** complete-refactoring · bound to SEAM-4-ACCEPTANCE B2/B3  
**Date:** 2026-08-12 · FREEZE respected (no AppHost edits)

## B3 — No silent credential fallback / no share of personal credentials

| Check | Result | Evidence |
|---|---|---|
| Silent fallback / demo / operator token paths in Sdk + Google/SF | **NONE found** | `rg` fallback/demo/operator/shared token over `DigitalBrain.Sdk`, `Modules/Google`, `Modules/SalesForce` → empty |
| Module-local token stores outside PrincipalTokenSlot | **NONE found** | `rg` TokenStore/RefreshToken/AccessToken durable stores under `src/Modules` (excl. GlobalUsings) → empty |
| Library publish transfers OAuth tokens | **NO** | `LibraryNeuron.Publish` publishes structure artifact + publisher PrincipalId only; grant-gated catalog — not token envelopes |
| Placeholder OAuth credentials | **REFUSED** | `McpOAuthOptions` rejects disallowed placeholders (`Configure a real application credential`) |

**B3 verdict:** tip-clean for audited paths. No code change required this slice.

## B2 — Tokens protected; never journaled as secrets

| Check | Result | Evidence |
|---|---|---|
| Protect/Unprotect on write | **YES** | `DurableMcpTokenCache` / `McpTokenPresence` use `IDurablePayloadProtector.Protect` before `PrincipalTokenSlot.Write`; Unprotect on read |
| PKCE verifier/code protection | **YES** | `McpAuthorizationNeuron` stores `ProtectedCodeVerifier` / protected code via purpose-prefixed Protect |
| StateProtectionKey wiring | **YES** | `DurablePayloadProtectionHosting` reads `DigitalBrain:Security:StateProtectionKey` |
| Plaintext tokens in journal fields | **NONE found in audited types** | Slot stores `byte[]` protected payloads; pending auth uses protected string fields |

**B2 verdict:** tip-confirmed for Sdk MCP token path. No code change required this slice.

## B4 — residual (not faked)

`McpAuthorizationNeuron.ResolvePrincipalChat` still parks to `chat` + principal-partitioned `"main"`. Remains Conversation-extract residual per acceptance B4 — **listed, not closed**.

## Next

D1: `dotnet build DigitalBrain.slnx -warnaserror` (no FREEZE file edits)  
D3: Product Grill rails smoke after D1 green
