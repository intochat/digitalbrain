# 04 — Connectors and Authentication

Treats Gmail and Salesforce as reference implementations of a general connector capability model, assesses the current contract and auth machinery, and defines the target contract needed to "connect to anything" without embedding each provider into INO or the kernel. Evidence: [file-audit/connectors-and-contracts.md](file-audit/connectors-and-contracts.md).

## What is genuinely strong (keep and generalize)

The **auth and token layer is the best-built security surface in the repository** and should be the template:

- **Token storage:** per-value DataProtection encryption at rest, strict app-scope vs user-scope isolation, keyed by principal (`connectors`). Tokens never appear in logs/traces/URLs (verified by negative assertions in `dotnet-tests`).
- **OAuth state/replay protection:** owner-bound protected `state`, SHA-256 fingerprints, fixed-time comparisons, durable one-shot processing claims (a used state cannot be replayed). This is genuinely robust.
- **Least-privilege scopes:** Google is confined to exactly `gmail.readonly` + `gmail.send`; no broad Mail scopes (`connectors:SEC-400` confirms the scope set).
- **Salesforce mutation pipeline** (`SalesforceMutationNeuron`): **preview → apply → verify** with explicit `AlreadyApplied` / `Conflict` / `VerificationFailed` outcomes — idempotent, reconciled, and the model every connector mutation should follow.
- **Salesforce OAuth uses full S256 PKCE.**

## What is weak or missing

| Finding | Sev | Issue |
|---|---|---|
| `connectors:ARCH-400` | High | `IConnector` is an **auth-lifecycle-only** interface (`ValidateConfig`/`BeginAuth`/`CompleteAuth`/`TestConnection`). It declares **no capabilities and no typed read/mutation surface** — every capability is hand-written per provider inside the kernel. |
| `connectors:ARCH-401` | High | Provider names are **hardcoded in kernel logic**: `ConversationNeuron.IsProviderTool` treats every non-Google provider as Salesforce; `InoMutationGrants.RequiredForTool` **fails open** for unlisted mutation tools. |
| `connectors:SEC-405` | High | `PackSignatureVerifier` + `PublisherTrust` exist but have **zero callers** — packs (a connector/behavior delivery mechanism) can be embodied without integrity/trust checks. |
| `connectors:SEC-400` | Med | **Google OAuth has no PKCE** (a dead `OAuthCodeVerifierKey` shows it was planned); the two OAuth state machines diverged (`ARCH-403`). |
| `connectors:PROD-400` | Med | **Gmail send dedup is non-atomic**: an `rfc822msgid:` search-then-send is vulnerable to search-index lag on retry; there is no `OutcomeUnknown` send status. |
| `connectors:ARCH-402` | Med | Salesforce and Google OAuth duplicate flow state machines instead of sharing one. |

## Does provider concern leak into the kernel?

**Yes, in two places** that must be closed for the OS model to hold:

1. **Hardcoded provider names in orchestration/grant logic** (`connectors:ARCH-401`). The kernel "knows" about Google and Salesforce by name. Adding a third connector requires editing kernel code and, because of the fail-open default, risks silently granting an unlisted tool.
2. **Capabilities are not declared by connectors** — they are implemented by bespoke neurons the kernel wires up (`GmailReadNeuron`, `SalesforceReadNeuron`, `SalesforceMutationNeuron`). There is no registry a new connector can register into.

Everything *below* the capability line (OAuth, token storage) is properly provider-isolated; everything *at and above* it (capability declaration, routing, grants, mutation orchestration) is not.

## Answer: can a 10th / 100th connector be added without kernel/INO edits?

**No.** Today, a new connector requires: new provider neurons, new hardcoded name branches in `ConversationNeuron`/`InoMutationGrants`, and manual wiring in hosting extensions. The auth lifecycle would be reusable; nothing above it is. This directly fails reference journey 5 ([01](01-product-north-star.md)).

## Gap table — current `IConnector` vs. target "connect to anything" contract

| Dimension | Current | Target |
|---|---|---|
| Auth lifecycle | ✅ `BeginAuth/CompleteAuth/ValidateConfig/TestConnection` | Keep; add token refresh/rotation/revocation as first-class contract methods. |
| Capability declaration | ❌ none | Connector declares typed capabilities (id, kind read/mutation, input/output schema, required scopes/grants, reversibility, rate-limit class). |
| Discovery / registration | ❌ hardcoded names | Connectors self-register into a `ConnectorRegistry`; kernel/INO resolve by capability, never by provider name. |
| Typed read | ❌ bespoke neurons | Uniform `ReadAsync(capabilityId, request)` returning minimized, typed results with provenance. |
| Typed mutation | ⚠️ Salesforce only, bespoke | Uniform `PreviewMutation` → `ApplyMutation` → `VerifyMutation` contract (generalize Salesforce's pipeline); mandatory idempotency key + `OutcomeUnknown`. |
| Grant mapping | ❌ fail-open, hardcoded | Capability declares required grants; unknown capability = deny. |
| Rate limits / backoff / pagination | ⚠️ per-provider ad hoc | Declared per capability; enforced by a shared connector-host wrapper. |
| Data minimization | ✅ (read results minimized) | Keep; make minimization a declared property of each read capability. |
| Reversibility | ⚠️ implicit | Declared per mutation capability; drives the preview/undo UX. |
| Signing/trust (for packaged connectors) | ❌ unused | Enforce `PackSignatureVerifier`/`PublisherTrust` before any connector pack embodies. |

## Target connector model (proposal)

```
IConnector (auth lifecycle)                     ← keep, extend with refresh/rotate/revoke
  ├─ declares → CapabilityManifest[]            ← id, kind, schemas, scopes, grants, reversible, rateClass
ConnectorRegistry                               ← self-registration; resolve by capability, not name
ConnectorHost (shared wrapper)                  ← rate limiting, backoff, pagination, minimization, tracing
  ├─ ReadCapability.ReadAsync(request)          ← typed, minimized, provenance-tagged
  └─ MutationCapability                          ← Preview → Apply(idempotencyKey) → Verify, OutcomeUnknown
INO / kernel                                     ← resolve capabilities generically; NO provider names
```

**Migration is low-risk:** the auth layer is reused unchanged; Gmail/Salesforce neurons become capability implementations behind the manifest; the hardcoded name branches are deleted and replaced by registry lookups. The Salesforce mutation pipeline is promoted to the shared mutation contract.

## Authentication model (assessment + target)

**On-demand initiation:** OAuth begins when a capability needs a scope the principal hasn't granted — present today for both connectors.

**Present strengths:** owner-bound state, replay protection, fixed-time compares, per-principal encrypted tokens, least-privilege Google scopes, Salesforce S256 PKCE.

**Required fixes:**
1. **Add PKCE to Google** (`connectors:SEC-400`) — the dead `OAuthCodeVerifierKey` shows the intent; wire it.
2. **Unify the two OAuth state machines** (`connectors:ARCH-402/403`) into one shared, tested flow.
3. **Token rotation/revocation as contract methods** — expiry/refresh exist; make rotation and user-initiated revocation first-class and surfaced in the permissions UI ([01](01-product-north-star.md) journey 6).
4. **Gmail send:** add an `OutcomeUnknown` status and an idempotent send (store a client-generated `Message-ID` before send; reconcile on retry) (`connectors:PROD-400`).
5. **Encrypt the DataProtection key ring** that protects the tokens (`kernel-hosting:SEC-200`) — otherwise the excellent per-value encryption is undermined at the root.

## Capability/permission UX (product)

The user must be able to see, per connector: which capabilities are granted, which scopes that implies, when granted, and revoke each — and every mutation capability's preview must state its reversibility. The data exists (grants, tokens, journals); the control surface does not yet. This is part of the MLP ([01](01-product-north-star.md)).
