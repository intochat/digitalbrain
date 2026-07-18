# DigitalBrain — Marketplace (pack · sell · buy · install)

> Status: **as-built audit + design memo**, not yet canonical. Builds on
> `docs/v5plan/DOMAINS.md` (the free install model) and `docs/v5plan/VISION.md`
> (v5 "The Cut"). Where this memo and v5 conflict, v5 wins until promoted.
> Written 2026-05-30.
>
> This memo answers one ask: **"a marketplace where a business analyst can
> author `.ino` files, pack them into a bundle, and sell them — and I can
> buy and install them. Super easy and shareable."**

---

## 1. TL;DR — most of this is already built

DigitalBrain already has a working **pack → compile → sign → publish → buy →
license → install** pipeline. This memo documents the as-built flow, names the
real types, and lists the honest gaps that remain before it ships.

```
author .ino  ──pack──▶  .bdom (zip)  ──publish──▶  MarketplaceNeuron
  (@price tag)            manifest.json              compiles each .ino,
                          *.ino sources              ECDSA-signs the manifest,
                          signature.dat              stores BundleInfo in the DB
                                                              │
  buy ◀── BuyBundleCommand ── user ── browse catalog ◀────────┘
   │
   └─▶ MarketplaceNeuron: record purchase ─▶ LicenseNeuron issues a signed
        license token ─▶ returned to the buyer
                                                              │
  install ◀── .bdom + license token ───────────────────────────┘
   │
   └─▶ LocalBundleInstaller: verify signature (ECDSA) + verify license
        entitlement (LicenseNeuron) + compile each .ino ─▶ register live
        neurons in the brain
```

**Free** bundles need no signature, no license, no payment — they install
straight through (this is the `DOMAINS.md` git-clone model, generalized to a
zip). **Premium** bundles (`@price` ≠ `free`) are gated by signature + license.

---

## 2. The bundle format (`.bdom`)

A bundle is a zip (`.bdom`) — the shareable unit a business analyst sells. It
contains:

| Entry | Purpose | Required? |
|---|---|---|
| `manifest.json` | Bundle identity + the neuron file list | yes |
| `*.ino` | One or more neuron source files | yes (≥1) |
| `signature.dat` | Base64 ECDSA signature of `manifest.json` | premium only |

```json
// manifest.json
{
  "bundleId": "acme/insurance-triage",
  "version": "1.0.0",
  "licenseToken": "<base64 — present on a purchased premium bundle>",
  "neurons": [
    { "fqn": "Acme.Insurance.Triage", "sourcePath": "Triage.ino" },
    { "fqn": "Acme.Insurance.Quote",  "sourcePath": "Quote.ino" }
  ]
}
```

The bundle's commercial terms live **in the `.ino` front-matter**, scanned by
`InoMetadataScanner` (`kernel/DigitalBrain.Kernel/Runtime/InoMetadataScanner.cs`):

```ino
# @price: 19.99
# @license: commercial-eula
# @requires: DigitalBrain.Ai.Chat
neuron Acme.Insurance.Triage
  "Classifies an inbound claim into a triage lane."
  ...
```

| Tag | Meaning | Default |
|---|---|---|
| `@price:` | `free`, or a price string (e.g. `19.99`) → marks the bundle **premium** | `free` |
| `@license:` | SPDX-ish license id or `source-included` | `source-included` |
| `@requires:` | an FQN the bundle depends on; verified against the contract catalog at install | (none) |

This is the v5 spirit: the `.ino` is the single source of truth — price and
license are declared next to the behavior, not in a separate store.

---

## 3. The pipeline, grounded in the code

### 3.1 Publish — `MarketplaceNeuron.PublishBundleAsync`
`kernel/DigitalBrain.Kernel/Runtime/Neurons/MarketplaceNeuron.cs`

1. Unzip; require `manifest.json`; match `bundleId`/`version`.
2. For each neuron: `InoCompiler.Compile(source)` — **no compile, no publish**
   (the v3 L6 gate, applied to the marketplace).
3. `InoMetadataScanner.Scan(source)` → resolve the bundle's price + license.
4. ECDSA-sign `manifestJson` with the marketplace key pair
   (`BundleSignatureVerifier.SignData`, nistP256 + SHA-256), persisted in the
   neuron's durable state.
5. Store `BundleInfo(bundleId, version, manifestJson, signature, price,
   license)` in `PostgresDbNeuron` (an in-memory, durable-list-backed neuron —
   *not* a real Postgres today).

### 3.2 Buy — `MarketplaceNeuron.BuyBundleAsync` (MKT-4: real Stripe checkout)

1. Look up the bundle in the catalog.
2. **Free bundle** → fulfill immediately (no payment due): record the purchase and
   `LicenseNeuron.IssueLicenseAsync` returns a signed token in the response.
3. **Premium bundle** → open a **Stripe Checkout session** through the connector
   facade `IStripeGateway.CreateCheckoutSessionAsync`
   (`sdk/DigitalBrain.SDK/Stripe/StripeGateway.cs`, wrapping Stripe.net
   `SessionService`). The response carries the `CheckoutUrl` + `CheckoutSessionId`
   and an **empty** `LicenseToken` — **no purchase row, no license yet**. Offline
   (no `Stripe:SecretKey`) the gateway returns a synthetic session so dev/test buy
   flows still work without keys.
4. Payment is confirmed asynchronously by Stripe → `checkout.session.completed`
   → `MarketplaceNeuron.ConfirmCheckoutAsync(stripeEventJson, signature)` verifies
   the event via the same connector (`IStripeGateway.VerifyEvent`, Stripe.net
   `EventUtility.ConstructEvent`/`ParseEvent`) and **only then** records the
   `PurchaseRow` + `IssueLicenseAsync`. Fulfillment is idempotent on
   `(userId, bundleId)`. Entitlement never precedes payment.

### 3.3 The license token — `LicenseNeuron`
`kernel/DigitalBrain.Kernel/Runtime/Neurons/LicenseNeuron.cs`

A self-contained, offline-verifiable grant:

```
base64( json{
  payload:   json{ bundleId, userId, issuedAt, nonce },
  signature: base64( ECDSA-sign(payload, licenseServerPrivateKey) ),
  publicKey: base64( licenseServerPublicKey )
} )
```

`VerifyLicenseAsync` checks three things: (1) the embedded signature verifies
against the embedded public key, (2) the payload's `bundleId`/`userId` match
the request, (3) the token is present in the DB entitlement table. (1)+(2) make
the token tamper-evident offline; (3) makes it revocable.

### 3.4 Install — `LocalBundleInstaller.InstallLocalAsync`
`kernel/DigitalBrain.Kernel/Runtime/LocalBundleInstaller.cs`

Installs from a `.bdom` zip **or** a directory (for local dev). Steps:

1. Parse `manifest.json`; scan each neuron's price/license/requires.
2. Verify every `@requires:` FQN resolves in the contract catalog.
3. **If premium** (and not `DigitalBrain:Marketplace:AllowUnsigned`):
   - require `signature.dat`; verify it against `MarketplaceNeuron`'s public
     key;
   - require `manifest.licenseToken`; extract its `userId`; verify via
     `LicenseNeuron.VerifyLicenseAsync`.
4. Compile each `.ino` and register it live through the
   `InterpretedNeuronRegistry`.

There are **two independent ECDSA key pairs**: the marketplace signs the
*bundle*, the license server signs the *entitlement*. A buyer's brain trusts
both public keys; tampering with either the code or the license fails the gate.

---

## 4. How this reconciles with `DOMAINS.md`

`DOMAINS.md` says "a domain is a public GitHub repo; `git clone` is the
installer; no paid layer." That remains true for **free** domains. The
marketplace is the **paid, signed superset**:

| | Free domain (`DOMAINS.md`) | Paid bundle (this memo) |
|---|---|---|
| Unit | GitHub repo of `.ino` | `.bdom` zip of `.ino` |
| Install | `git clone` | download `.bdom` + license |
| Trust | user vets the repo | ECDSA signature + license token |
| Discovery | GitHub topic `digitalbrain-domain` | catalog (`GetBundlesQuery`) + topic |
| Payment | none | Stripe (see §5) |
| Registration | same `InterpretedNeuronRegistry` | same `InterpretedNeuronRegistry` |

Both end at the same place — neurons registered in the brain. The marketplace
adds signing, entitlement, and payment around the same install core. No second
runtime.

---

## 5. The honest gaps (what's NOT done)

Applying the v5 "question it / cut it" discipline — these are the real holes:

1. **Published bundles drop their content.** `PublishBundleAsync` receives
   `zipBytes` but persists only `manifestJson` + `signature` in `BundleInfo`.
   So a buyer **cannot actually download the bundle** — the pack→sell→**buy**
   link is broken. *Fix:* persist the zip (blob column / object store) and add
   a `DownloadBundle` path. **DONE (MKT-2):** `PublishBundleAsync` now persists
   `zipBytes` on `BundleInfo` and `MarketplaceNeuron.DownloadBundleAsync` serves
   it, entitlement-gated (premium requires a license row). The remaining work is
   the install side calling it (gap #3 / MKT-3).
2. **Stripe is mocked — ✅ done (MKT-4).** `BuyBundleAsync` now opens a real Stripe
   Checkout session (premium) and returns **no** license; payment is confirmed by a
   `checkout.session.completed` webhook, which `MarketplaceNeuron.ConfirmCheckoutAsync`
   verifies (via the shared `IStripeGateway` connector facade) before recording the
   purchase + issuing the license. Rather than fork a second connector, both checkout
   creation and webhook verification ride one facade (`StripeGateway`, the only place
   Stripe.net is called). There is exactly one entitlement entry point —
   `ConfirmCheckoutAsync`, which verifies through the facade then fulfills — so a license
   can never be granted on an unverified event. License issuance stays in the kernel (the
   SDK connector layer cannot grant entitlements). Covered by `MarketplaceCheckoutTests`.
   *Operator wiring (not code):* a publicly reachable webhook endpoint (Stripe CLI
   `stripe listen --forward-to …`, or the Aspire-exposed kernel URL) forwards Stripe's
   POST to `ConfirmCheckoutAsync`, and a sandbox `Stripe:SecretKey` / `Stripe:WebhookSecret`
   are supplied via the secret vault rather than committed.
3. **`InstallMarketplaceNeuron` — ✅ done (MKT-3).** It now `DownloadBundleAsync`
   → re-attaches the publisher signature + buyer license as separate
   `signature.dat`/`license.dat` entries (Option A, manifest untouched) →
   installs via the real `LocalBundleInstaller`, which verifies signature +
   entitlement, compiles each `.ino`, and registers the neurons. The installer
   reads the token from `license.dat`. Covered by `MarketplaceInstallTests`.
4. **No discovery / storefront UI.** `GetBundlesQuery` returns a catalog, but
   there's no RFW storefront surface and no GitHub-topic search for paid
   bundles. *Fix:* an RFW marketplace surface (V5-4) + reuse the
   `digitalbrain-domain` topic search.
5. **Zero automated tests** of the trust chain or the flow. *Fix:* this memo's
   companion test (`MarketplaceTrustChainTests`) covers the crypto core first;
   the full publish→buy→install E2E follows once gap 1 lands.

---

## 6. Proposed invariant (V6-M)

**A premium bundle activates only if both its code signature and its license
entitlement verify.** The bundle is ECDSA-signed by the marketplace; the
entitlement is an ECDSA-signed, DB-revocable license token bound to
`(bundleId, userId)`. Commercial terms (`@price`, `@license`, `@requires`) are
declared in the `.ino` front-matter — one source of truth. Free bundles bypass
signature/license but use the same registration path. No second runtime, no
custom registry server — discovery rides the same GitHub-topic model as
`DOMAINS.md`.

Relation to existing invariants: **L6** (no compile → no publish) is enforced
at publish; **V5-5** (domains are repos) generalizes to "paid bundles are
signed zips"; **V4-3** (brain isolation) holds — installs land in the calling
brain's scoped registry.

---

## 7. Slices to close the gaps (additive, each shippable)

| Slice | Deliverable | Closes |
|---|---|---|
| **MKT-1 — Trust-chain tests** ✅ | Cover `BundleSignatureVerifier` (sign/verify/tamper) + `InoMetadataScanner` (free/premium detection). | gap 5 (core) |
| **MKT-2 — Bundle content store** ✅ | Persist `zipBytes` on publish; entitlement-gated `DownloadBundleAsync` + grain test. | gap 1 |
| **MKT-3 — Real install** ✅ | `InstallMarketplaceNeuron` downloads → repacks signature.dat+license.dat → `LocalBundleInstaller`. | gap 3 |
| **MKT-4 — Stripe checkout** ✅ | `IStripeGateway` (checkout + verify) + webhook-gated `ConfirmCheckoutAsync` → `IssueLicense`. | gap 2 |
| **MKT-5 — Storefront RFW** | Marketplace surface (browse/buy/install) + topic search. | gap 4 |
| **MKT-6 — Publish→buy→install E2E** | Full silo test over the synapse path. | gap 5 (E2E) |

Critical path: **MKT-1 → MKT-2 → MKT-3** make buy-and-install actually work;
MKT-4 (payment) and MKT-5 (UI) layer on; MKT-6 locks it.

---

## 8. The end-to-end example

> A business analyst sells an insurance-triage bundle; you buy and install it.

1. **Pack.** The BA writes `Triage.ino` (`# @price: 19.99`) + `manifest.json`,
   zips them into `insurance-triage.bdom`.
2. **Publish.** `PublishBundleCommand` → the marketplace compiles the `.ino`,
   signs the manifest, lists `acme/insurance-triage` in the catalog (MKT-2
   stores the zip).
3. **Browse & buy.** You `GetBundlesQuery` the catalog, `BuyBundleCommand` →
   Stripe Checkout (MKT-4) → on confirmation, a license token is minted.
4. **Install.** Your brain downloads the `.bdom` (MKT-3), `LocalBundleInstaller`
   verifies the marketplace signature + your license, compiles `Triage.ino`,
   and registers `Acme.Insurance.Triage` as a live neuron.
5. **Use.** The new neuron appears on your Living Canvas and answers synapses
   like any other — a stranger's expertise, running in your brain in seconds.

Steps 1–4 all work in code now (MKT-2 stores the zip, MKT-3 installs, MKT-4 gates
the license on confirmed Stripe payment). MKT-5 (storefront RFW) and MKT-6 (full
silo E2E) remain.

---

## 9. Companion docs

- [`../v5plan/DOMAINS.md`](../v5plan/DOMAINS.md) — the free install model this extends.
- [`FEDERATION.md`](FEDERATION.md) — cross-brain agent collaboration (a bought bundle can expose federated neurons).
- [`../v5plan/VISION.md`](../v5plan/VISION.md) — the v5 invariants (L6, V5-5) the marketplace honors.
