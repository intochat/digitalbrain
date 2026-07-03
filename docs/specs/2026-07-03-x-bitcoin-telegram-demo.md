# X → Bitcoin Price → Telegram Demo — Design

Status: draft, pre-implementation-plan.

Supersedes `docs/specs/2026-07-03-self-improvement-loop.md` as the active work item (parked, not
deleted, on branch `spec/self-improvement-loop`). That spec was too large and speculative for an
imminent team demo; this one targets a small, concrete, mostly-already-supported end-to-end proof.

## Why

This is the concrete cross-channel demo `CONTINUITY.md` already flagged as the next candidate — the
original (never-built) capstone: "Telegram input → cross-channel logic → visible result," this time via
a plain-English request: *"When there's a new post on X from Elon, check the Bitcoin price and send it
to me on Telegram."*

The core mechanism this proves — a plain-English request causing DigitalBrain to build and run an
automation — **already exists**: `run_code_foundry` (MCP tool → `FoundryRequest` → `CodeGenNeuron`
turns the spec into C# via an LLM → checkpoint → run/deploy), with its own Reqnroll coverage already
started (`CodeFoundry.feature` / `CodeFoundrySteps.cs`). This spec does not rebuild that — it proves one
concrete, reliable, well-tested cross-channel automation, following the exact patterns already proven
for Telegram (`TelegramExperience.feature`'s reactive-loop and N+1-reactivity scenarios).

## Scope decisions

- **Simulated X trigger**, not real X/Twitter API integration. Real API access needs a paid tier + auth
  management — a new integration surface on par with the existing Google ino work, and unnecessary to
  prove the reactive automation logic. A `simulate_x_post(author, text)` MCP tool fires
  `Signal("XPostReceived", {author, text})`; the demo pack reacts to that signal regardless of where it
  came from, so a real poller is a later, isolated add-on that never touches the pack's logic.
- **Hand-authored and tested pack**, not live-generated for the demo itself. The live demo shows the
  automation working reliably end-to-end. `run_code_foundry`'s plain-English generation is real and
  already has its own coverage, but isn't staked on landing correctly live in front of the team —
  qwen2.5-coder:1.5b's output isn't guaranteed to compile/behave correctly first try.

## Components

Three small additions; everything else (Telegram egress, pack config, checkpoint, embodiment) is
existing, already-tested infrastructure.

1. **`simulate_x_post(author, text)` MCP tool** — mirrors the existing `fire_synapse` tool shape. Fires
   `Signal("XPostReceived", {author, text})` on the broadcast stream.
2. **`MarketDataNeuron`** — new, small, Kernel-side infra neuron, same shape as `LlmResponderNeuron` (no
   isolated-ino peer project needed for one HTTP-calling neuron). `IHandle<Signal>` filtering
   `signal.Name == "CheckBitcoinPrice"`, calls a real public API (CoinGecko, no auth required) through an
   `IMarketDataApiClient` wrapper — mirrors `DigitalBrain.Google`'s `I*ApiClient` pattern so tests can
   fake it deterministically — then broadcasts `Signal("BitcoinPriceChecked", {price})`.
3. **The demo pack** — a small `IPackBehavior`, same shape as `PersonalAssistantNeuron`/
   `TelegramResponderNeuron`:
   - Reacts to `Signal("XPostReceived")` matching a configured `watched_author` (declared via the
     existing `PackConfigField`/`RequiredConfig` mechanism, same as `telegram_token`) → fires
     `Signal("CheckBitcoinPrice")`.
   - Reacts to `Signal("BitcoinPriceChecked")` → fires `Signal("TelegramReplyRequested", {chatId, text})`,
     reusing the existing Telegram egress path unchanged.

Authored via the existing fast TDD loop (`docs/authoring-a-bundle.md` pattern: write the failing test,
then the pack source) — using a signal-driven harness equivalent to `BundleHarness` rather than the
UI-tree assertions (this pack has no UI surface).

## Testing strategy

New Reqnroll feature, same shape and same underlying harness/egress-bus-watching infrastructure as
`TelegramExperience.feature`'s reactive-loop scenario — no new test machinery invented:

```gherkin
Feature: X post triggers a Bitcoin price alert on Telegram

  @distribution @e2e @demo
  Scenario: X post from watched author triggers a Bitcoin price alert on Telegram
    Given the X-Bitcoin-Telegram demo pack is installed with watched author "elon"
    And the Telegram configuration token "tok-123" is provided
    And the market data client is stubbed to return a Bitcoin price of "$61,000"
    When a simulated X post from "elon" arrives with text "big news"
    Then a "CheckBitcoinPrice" signal fires
    And a "TelegramReplyRequested" reply reaches the egress bus with text containing "$61,000"
```

`IMarketDataApiClient` gets a fake implementation for tests (deterministic price, no real network calls
in the suite) — same pattern as the Google API client wrappers. The existing `CodeFoundry.feature`
coverage independently documents the `run_code_foundry` plain-English path — that's the "vision" story,
proven separately, not gated on this demo's reliability.

## Non-goals

- Real X/Twitter API polling/webhook integration (later, isolated add-on).
- Live code generation as part of the demo moment (already covered by existing `CodeFoundry.feature`).
- Any change to the self-improvement-loop spec (parked on its own branch).

## Phased delivery

`IMarketDataApiClient` + `MarketDataNeuron` (testable in isolation, no pack yet) → demo pack (fast
harness test first) → Reqnroll end-to-end scenario → `simulate_x_post` MCP tool for the live trigger.
