# Ino.Billing — design doc

Status: draft, 2026-05-03. Owners: open. Supersedes: nothing (greenfield).

## Why this doc exists

The pricing page is the trivial part. The hard part is meter design and trust isolation. ino's promise is **$20/mo flat + pay-as-you-go on cloud, tokens, and requests; no tariffs**. That's a unit-economics commitment: every neuron in every domain has to emit usage so something can sum it. If we ship the page before the meter exists, the page lies. This doc fixes the architecture before any UI work.

Three things to settle:

1. **Where billing lives** — silo topology and trust boundary.
2. **What flows through it** — the `UsageSynapse` contract.
3. **Build vs. buy on the meter itself** — own all of it, or let Stripe/Lago/OpenMeter own the metered-subscription bookkeeping.

## Topology — a private `Ino.Billing` silo

`Ino.Billing` is a peer silo to `Ino.Identity`, `Ino.Domains.Travel`, `Ino.Domains.Taxi`. Marker: `Billing : IDomain` (`DomainId "billing"`). Wired in `Ino.AppHost` like any other domain. Three properties make it private:

- **No public gateway route reaches it.** `Ino.Gateway.Grpc` exposes Chat + future read APIs; *none* point at billing-internal grain interfaces. The Flutter authenticated billing UI (current usage, invoices, payment method) talks to a small set of read-only DTO endpoints on the kernel gateway, and the kernel forwards them as synapse fires — never as direct grain refs.
- **Sensitive grains pinned via placement filter.** `PaymentMethodGrain`, `StripeAdapterGrain`, `ChargeIntentGrain` use `PinToSiloPlacementFilter` (already in the codebase) so they activate only on `Ino.Billing`. Even if Travel is compromised, it cannot enumerate cards.
- **Stripe key + payment-method tokens never leave that process.** API parameter is read only by `Ino.Billing`. PCI scope = one silo. The `Ino.Aspire.Hosting` extension `WithDomain("billing")` adds a `stripe-secret-key` parameter (secret) and an optional `lago-api-key`/`openmeter-api-key`.

Note: this is *trust* isolation, not network isolation. All silos share the Orleans cluster. The boundary is: **what code is allowed to talk to Stripe, and what data is allowed to leave a process.** Other silos talk to billing exclusively through `UsageSynapse` fires (one-way) and `BillingQuery` request/response synapses for the small read surface.

## The three surfaces — keep them separate

| Surface | Lives in | Auth | Talks to billing how |
|---|---|---|---|
| Public `/pricing` page | `Ino.Kernel/wwwroot/marketing/` (static) or new tiny React route | none | doesn't — it renders the offer, no API |
| Authenticated billing UI (usage, invoices, payment method) | Flutter app under `clients/ino.flutter` (route `/billing`) | gateway JWT | gRPC → kernel gateway → `BillingQuery` synapse → `Ino.Billing` |
| Metering + charging | `Ino.Billing` silo, private | grain-internal only | consumes `UsageSynapse` fires from every domain |

The marketing page is a render-only artifact. Don't put it in its own silo for one page.

## The `UsageSynapse` contract

This is the single primitive every domain emits. Definition belongs in `Ino.Billing.Contracts` (new project, referenced by every silo that bills).

```csharp
[GenerateSerializer]
public sealed record UsageSynapse(
    Caller   Caller,                 // who: user/tenant/anonymous
    DomainId Domain,                 // travel | taxi | kernel | ...
    string   ServiceKey,             // stable string id of the unit being billed (e.g. "travel.plan-trip", "kernel.chat-turn")
    Surface  Surface,                // Api | Telegram | MiniApp | Scheduled | AgentInternal
    UsageDimensions Dimensions,      // see below — multiple cost axes per event
    DateTime OccurredAtUtc,
    Guid     IdempotencyKey,         // dedup on the consumer side; identical key = same event
    string?  TraceId,                // OTel trace id for correlation, optional
    PrivacyMode Privacy              // Default | ZeroTrace
) : ISynapse;

[GenerateSerializer]
public sealed record UsageDimensions(
    long?    Tokens,                 // sum of input + output, model-agnostic for now
    decimal? CloudMillicpuSeconds,   // cpu-millis × seconds (= 1 vCPU-second when 1000)
    int?     Requests,               // discrete operation count, usually 1
    Dictionary<string, decimal>? Extras  // domain-specific overages: SerpApi calls, MCP tool calls, …
);
```

Three dimensions, all optional, summed independently. A chat turn might emit `Tokens=4200, Requests=1, CloudMillicpuSeconds=180`. A scheduled trip-radar refresh might emit `Tokens=0, Requests=1, Extras={"serpapi.calls": 3}`.

**`IdempotencyKey` matters.** At-least-once delivery means duplicates. The `BillingNeuron` keeps a sliding window of recent keys per user; duplicates are dropped silently (with a counter on `ino.billing.duplicates`).

**Privacy gate** mirrors TripRadar's `ZeroTrace`: paid users with privacy mode set get the event aggregated for billing math but not persisted as a per-event row. Implementation detail: keep a counter grain that increments without journaling.

## What ino's billing pipeline reuses from TripRadar

TripRadar already runs a working metered-billing stack against Stripe. The valuable bits to mirror (or, longer-term, *forward into* Ino.Billing while TripRadar is still a standalone product):

| TripRadar piece | What it does | ino equivalent |
|---|---|---|
| `PostSuccessUsageEventBehavior<TRequest,TResponse>` | MediatR pipeline behavior: after success, look up token cost, honor `ZeroTrace`, write `UsageEvent` | An IAW agent-pipeline middleware that fires `UsageSynapse` after each successful tool/turn. Same shape, expressed as an `Agent` middleware instead of MediatR. |
| `IUsageEventWriter` + `UsageEventRepository` | Normalized write API + read aggregations | `BillingNeuron.OnUsageAsync(UsageSynapse)`, journaled; aggregations served by `BillingQueryNeuron` |
| `IServiceTokenCostRepository` | Per-service-type token cost lookup | `ServiceCostRegistry` — `ServiceKey → CostFunction(dimensions) → cents`. Cluster-wide, hot-reloadable, seeded from a JSON resource so price changes don't need a silo restart. |
| `UsageEventSourceType` { Api, Scheduled, Telegram, Ai } | Which surface drove the request | `Surface` enum, supersetted: { Api, Telegram, MiniApp, Scheduled, AgentInternal, ToolCall } |
| `GET /api/v1/usage/events` (timeline + paged events + summary) | Profile usage analytics | `BillingQuery` synapse with the same shape — `Summary { current, limit, remaining }`, `Timeline[] { date, tokens, events }`, paged `Events[]`. The Flutter `/billing` route renders this. Identical JSON contract makes it easy to share rendering code if we later port `TripRadar.WebUI/pages/profile/ui/billing/UsageSection.tsx` into ino. |
| `OverageBillingRecord` + `usePayAsYouGo` flow | Toggle pay-as-you-go, surface overage charges per month | `PayAsYouGoToggleSynapse` + `OverageQuery` synapse. The $20/mo offer makes this the *normal* path, not an opt-in like TripRadar. |
| `StripeUsageSummaryInfo` / `StripeUsageMetricInfo` | Stripe Meter Events integration | Used as-is by `StripeAdapterGrain` if we go option B below. |

**TripRadar-as-domain forwarding.** Once TripRadar's Travel domain is fully expressed as ino neurons (per `project_travel_domain_vision.md` in memory), the `PostSuccessUsageEventBehavior` doesn't get rewritten — it gets *replaced* by the agent middleware that fires `UsageSynapse` to `Ino.Billing`. Until then, while TripRadar is still standalone with its own Postgres, the easiest path is a **bridge**: TripRadar's `IUsageEventWriter` gets a second implementation that *also* fires a `UsageSynapse` over gRPC into the kernel. Costs stay tracked in TripRadar's database for that product's own UI, but ino sees the event too. One-line opt-in via DI registration.

## Build vs. buy on the meter — **decided: option B (Stripe Meter Events)**

Three options on the metered-subscription engine itself (the thing that turns "user X consumed 4.2M tokens this month at $0.012/1k" into a charge on the 1st):

| Option | Pros | Cons |
|---|---|---|
| A. **Custom inside `Ino.Billing`** — own grain that journals usage, runs monthly cutover via Orleans reminders, calls Stripe `PaymentIntent` directly | Most "neurons all the way down". Zero external billing deps. Total flexibility on price-function shapes (fractional, decay-weighted, multi-dim) | Idempotency, dunning, proration, refund flows, tax (VAT/GST), currency, dispute handling, invoice PDFs — all real surface area. Several engineer-months of bookkeeping that adds zero customer value. |
| B. **Stripe Billing with metered prices + Stripe Meter Events** — `Ino.Billing` is the meter source-of-truth, Stripe handles subscription, invoices, payments, dunning, tax, refunds | Battle-tested. Tax/VAT/dispute handled. TripRadar already integrates this way. Invoices and payment portal come for free. Idempotency keys are first-class. | Stripe is the system of record for *charges*. Price function has to be expressible as Stripe meter events. Multi-dim cost is N meters, one per dimension. |
| C. **Lago or OpenMeter (open-source) → Stripe** — usage stored in the OSS meter, monthly invoice pushed to Stripe | More flexible price functions than raw Stripe meters. Self-hostable. | Two systems to operate instead of one. Lago is still maturing. Doesn't avoid Stripe for actual money movement. |

**Decided: B (2026-05-04).** ino owns the synapse, the privacy gate, the price function (cost in cents per dimension), and the read API. Stripe owns the boring parts: subscription state, invoice generation, dunning, tax, refunds, dispute UI. The `UsageSynapse → BillingNeuron → StripeAdapterGrain` chain emits one Stripe Meter Event per dimension with the synapse's `IdempotencyKey` as the Stripe `identifier`. This is also exactly what TripRadar already does, which means the glue code is mostly already written.

Trip-wire to revisit: if we hit a price function Stripe meter semantics can't express (e.g. decay-weighted memory pricing per the v0.2 vision), kick option A back open as a per-dimension override — keep Stripe for the subscription envelope, run a custom meter for the one dimension that needs it.

## Open questions to settle before code

1. **What counts as a "cloud-minute"?** Three honest options:
   - (a) Wall-clock time inside neuron grain calls. Simple, but doesn't price idle activations.
   - (b) Orleans grain CPU-time, sampled via `EventCounters`. Closer to truth, harder to attribute.
   - (c) Just bill `Requests` + `Tokens`, drop "cloud" from the marketing copy. Honest if we can't measure (a) or (b) reliably yet.
   - **Recommendation:** start with (c) for v0.1 marketing, instrument (a) in parallel for v0.2, never bill (b) — it's not legible to users.
2. **What's the floor for the included $20?** Either "X tokens + Y requests, then pay-as-you-go" or "everything is pay-as-you-go but the first $20 of usage is bundled in the subscription." The second is simpler to explain ("$20 = $20 of usage credits + access") and matches the no-tariffs claim. **Recommend: prepaid $20 of usage credit, refilled monthly, top-ups via Stripe.**
3. **Refund policy on used credit?** Stripe handles the mechanic; we have to pick the policy. Apple gives you a 14-day right of withdrawal in EU. **Recommend: pro-rate the unused credit on cancellation, no refund on consumed credit.**
4. **Per-domain markup vs. transparent passthrough?** When Travel calls SerpApi at $0.005/call, do we charge $0.005 or $0.0075? **Recommend: transparent passthrough on third-party costs (line-itemed in `Extras`), markup only on ino-native units (tokens, requests, cloud-minutes).**

## Slice plan — what ships first

Don't try to do this in one branch. Three slices, each independently shippable.

**Slice 1 — `UsageSynapse` + `BillingNeuron` skeleton, no money.**
- New project: `Ino.Billing.Contracts` (record types above).
- New silo: `Ino.Domains.Billing` with `BillingNeuron` (journaled, sums per-user per-month dimensions).
- One domain wired to fire: kernel `ChatNeuron` after each turn.
- `BillingQuery` synapse returns `Summary + Timeline`.
- No Stripe, no UI yet.
- **Done when:** Aspire dashboard shows `UsageSynapse` traces from kernel → billing, and a CLI test fires a synthetic usage event and reads it back.

**Slice 2 — Stripe metered subscription + payment method.**
- `StripeAdapterGrain` (placement-pinned to `Ino.Billing`).
- `PaymentMethodGrain` per user, holds Stripe `payment_method` id.
- `BillingNeuron` forwards each `UsageSynapse` to Stripe Meter Events.
- One Stripe product, three meters (`tokens`, `requests`, `cloud_seconds`), one $20 base price.
- Public marketing page: static, in `Ino.Kernel/wwwroot/marketing/pricing.html`. One card. Apple-style.
- **Done when:** test user signs up via Stripe Checkout, makes a chat call, and sees the usage event reflected in Stripe dashboard the same minute.

**Slice 3 — authenticated billing UI in Flutter + travel/taxi domains emitting.**
- Flutter `/billing` route: current month, usage timeline, payment method, invoice list.
- Travel and Taxi domain agents wired with the IAW middleware that fires `UsageSynapse`.
- TripRadar bridge (option from §"What ino reuses"): standalone TripRadar fires usage to ino, Travel domain (when it lives in ino) fires natively.
- **Done when:** a 6-hop trip plan run end-to-end shows up in Flutter `/billing` with per-domain breakdown.

## Out of scope for this design

- L1/L2/L3 self-improvement billing (e.g. who pays for a user-generated neuron's compute) — separate doc, post-v0.1.
- Cross-tenant rev-share with domain authors (the "App Store for neurons" angle) — separate doc, after Slice 3.
- Tax/VAT custom logic — Stripe Tax handles it; we don't.
- Annual / promo / coupon flows — copy from TripRadar after Slice 2 is stable.
