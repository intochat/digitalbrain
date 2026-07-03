# Client Demo Script — DigitalBrain

**Runtime:** local, via `aspire run` from `brain/`. Windows desktop Flutter client (the default
`aspire run` launches). Total target time: ~7–10 minutes.

Every phrase below is pulled verbatim from an automated test that already asserts on it — see
"Tested by" under each beat. If you deviate from the exact wording on stage, the guaranteed
outcome no longer applies.

---

## Pre-demo setup (do this before the client arrives)

1. `aspire run` from `brain/`. Confirm via `aspire` CLI or MCP `list_resources` that all of:
   kernel ×3 replicas, Ollama, Azurite, MCP, and the Windows Flutter client report healthy.
2. Confirm the LLM path: per DEMO-PLAN.md decision **D-DEMO-1**, the demo is scoped to **Azure
   OpenAI `gpt-4o-mini`** (fast, reliable for a live audience), with Ollama as offline fallback
   only. Verify the Azure OpenAI key/endpoint is set in the environment/config *before* boot —
   check with the repo owner if `AppHost.cs`'s `WithLLM<TModel>()` wiring for this hasn't
   landed yet (see Fallback 1 below).
3. Confirm `demo/sample.xlsx` is present and reachable from the demo machine's file picker
   (e.g. on the Desktop, or wherever the live walkthrough will browse from). It's checked into
   `brain/demo/sample.xlsx` — copy it to wherever's fastest to reach mid-demo.
4. `DIGITALBRAIN_ENABLE_TELEGRAM` must be **unset/false** — no Telegram mirror running during
   the demo (kill switch, see below).
5. Two full dry runs of the sequence below, on the actual demo machine, before the client
   arrives. Note actual timings you observe next to each beat.

---

## The walkthrough

### Beat 1 — App opens straight into chat (~15s)

Launch the app. It opens on `/` — the chat screen, no sign-in, no other UI.

**Expect:** the empty-state cue card is visible:
> "I'm your DigitalBrain. Ask me anything, drop an Excel file, or ask for the Bitcoin price."

This is also the client's cue card — read it out loud if the room needs a prompt for what to
try. (Verified verbatim in `lib/features/chat/chat_screen.dart`'s empty state.)

### Beat 2 — Real chat reply (~1 min)

Type exactly: **`hello, what can you do?`** → press Enter (or Send).

**Expect:** a sending indicator appears briefly, then a real model-generated assistant reply
renders as a chat bubble, in well under 5 seconds on the Azure OpenAI path.

**Tested by:** `DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs` (`InoRequest_Emits_Assistant_Reply_Surface_To_FlutterUi`) — asserts an assistant `UiSurface` is delivered for this exact prompt. Note: that test runs with no `IChatClient` configured (asserts the `[no-llm]` fallback text), so it proves the *pipe* works end-to-end, not the live model's actual wording — the live reply content itself is only verified by rehearsal, not by this test.

### Beat 3 — Excel → rich table (~1–2 min)

Click the attach (📎) button, or drag-and-drop `sample.xlsx` onto the chat.

**Expect:** a "📎 Attached sample.xlsx" user bubble appears immediately, then shortly after, an
assistant bubble renders a heading ("sample.xlsx") followed by a formatted table: 6 columns
(Month, NorthUnits, NorthRevenue, SouthUnits, SouthRevenue, TotalRevenue) × 8 rows (Jan–Aug).

**Tested by:** `DigitalBrain.Tests/Ino/InoNeuronTabularDataTests.cs`
(`TabularDataIngested_Emits_Heading_And_Table_Surface_To_FlutterUi`) — asserts the delivered
surface contains a heading + `ui:Table` with the parsed headers/rows. Parser correctness
(headers, rows, per-column stats) covered separately by
`DigitalBrain.Tests/TabularData/TabularDataParserTests.cs`.

### Beat 4 — Follow-up question answered from the data (~1 min)

Type exactly: **`which region had the highest total?`**

**Expect:** the assistant answers "North" (North = $186,000 vs South = $160,000 — the numbers
are round and addable on sight if you need to sanity-check the answer live).

**⚠️ Rehearsal risk, not fully covered by an automated test:** the only existing automated
coverage for the follow-up path is `TabularDataIngested_Journals_Context_So_Followup_Question_Sees_The_Data`,
which asks *"what was the total revenue?"* (a different phrase) and only asserts the reply is
non-empty and that the uploaded file's context was journaled — it does **not** assert the
answer is factually "North." Getting the *correct region* out of this exact phrase depends on
live LLM reasoning over the injected context, which no test pins down. **Verify this specific
phrase and its answer live during rehearsal** before trusting it on stage; if it's flaky,
either fall back to a more leading phrasing you've verified live is a phrasing that works
(clearly a script-during-rehearsal decision, not one I can make from tests alone) or drop this
beat and stay on Beat 3's table as the Excel proof point.

### Beat 5 — Bitcoin agent (~30s, optional per cut lines)

Type exactly: **`what's the bitcoin price?`**

**Expect:** an assistant reply containing a live, formatted USD price (e.g. "The current Bitcoin
price is $XX,XXX.XX."), fetched live from CoinGecko.

**Tested by:** `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`
(`Send_InoRequest_BitcoinPriceIntent_DeliversFormattedPriceSurface`) — sends this exact phrase
through the same `GatewayService.Send` path the client uses, with a faked price, and asserts the
exact price string lands in the delivered surface. This is the strongest-covered beat: the
literal phrase, the literal path, and the literal output shape are all pinned by the test — only
the *live* CoinGecko response content is untested (by design, it's live data).

### Beat 6 — N+1 marketplace live install (optional, only if time allows)

Not built for this pass (DEMO-PLAN.md marks it "only if ahead of schedule"; skipped this round).
If pursued later: install a seeded pack live via `InstallFromMarketplace` and show new behavior
working immediately, no restart — everything needed already exists, this would be a script +
dry-run item, not new code.

---

## Fallbacks

1. **Azure OpenAI down** → flip to Ollama. Per DEMO-PLAN.md, this should be driven by
   `DigitalBrainOptions.WithLLM<TModel>()` in `AppHost.cs` — **confirm with the repo owner
   before the demo whether that wiring is finished**, since as of the last check it still had
   hardcoded lines that could silently override the typed config. If Ollama is the fallback,
   pre-warm the model beforehand (send one throwaway prompt before doors open) — first-inference
   latency has been observed to run well past 100s cold in this environment.
2. **CoinGecko down** → skip Beat 5 entirely; it's marked optional in this script. Don't
   improvise a retry live.
3. **Kernel crash / hang mid-demo** → restart procedure: stop `aspire run` (Ctrl+C), confirm no
   stale `DigitalBrain.Kernel.exe` processes are holding build-output locks, `aspire run` again.
   Target under 2 minutes — measure this explicitly during the two rehearsals and update this
   line with the actual observed time.

## Kill switches (minimum moving parts on stage)

- `DIGITALBRAIN_ENABLE_TELEGRAM` — unset or `false`.
- No diagnostic/dev routes visible from the demo path (`/gallery`, `/canvas`, `/experience`,
  `/spike` all still exist behind the router but are not part of this walkthrough — don't
  navigate to them on stage).

---

## Rehearsal results (2026-07-03, this environment)

Two live runs against a real `aspire run` boot, driven directly at the `Ask` RPC / `/upload`
endpoint level (not through the Flutter GUI — see report for why). **Two of three core beats
are currently demo-blocking. Do not present this on stage until both are fixed.**

**Cold boot:** all resources (3 kernel replicas, Ollama, Azurite, Flutter, MCP) reported
`Healthy` within seconds of `aspire run` starting — much faster than the "150s+" latency noted
in an earlier session. Encouraging, but re-confirm on the actual demo machine, not this one.

**Beat 2 (chat):** ⚠️ inconsistent. Run 1: `"BRANCH: learn about the context provided and
generate a response based on that."` (incoherent). Run 2: `"I'm here to help! How can I assist
you today?"` (coherent but generic — doesn't mention chat/Excel/Bitcoin capabilities the way a
stronger model would). Root cause below.

**Beat 4 (follow-up question):** ❌ **factually wrong both runs.** Run 1: a rambling non-answer
suggesting the user write their own Python/pandas to compute it. Run 2: **`"The highest total
revenue was in South with a total of $18,200."`** — wrong region (North is correct, $186,000)
and the dollar figure doesn't match either region's real total ($186,000 / $160,000) — it's a
hallucinated number, not a miscalculation. The model *does* know the file's context exists (it
named "sample.xlsx" correctly in run 1) but cannot reliably reason over the injected data.

**Beat 5 (bitcoin price):** ❌ **failed both runs, identically** — `RpcException: ... 403
(Forbidden)` from CoinGecko, ~instant both times (not a timeout/rate-limit backoff pattern).

### Root causes (diagnosed, not yet fixed — out of this phase's scope to change unilaterally)

1. **Beats 2 & 4 — this environment is running Ollama `qwen2.5-coder:1.5b`, not Azure OpenAI.**
   Confirmed two ways: the live `aspire` resource graph shows every kernel replica referencing
   only the `qwen` Ollama model resource — no Azure OpenAI resource exists in the graph at all —
   and the actual reply content/quality matches a small code-completion model struggling with
   general chat and RAG-style reasoning, not gpt-4o-mini. This directly contradicts decision
   **D-DEMO-1** in DEMO-PLAN.md ("Ollama qwen2.5-coder:1.5b is too weak/slow... Default: wire
   the demo scope to Azure OpenAI gpt-4o-mini"). **This is very likely the fix for both Beat 2
   and Beat 4** — a proper instruction-tuned model should both converse coherently and actually
   use injected context correctly. `AppHost.cs`'s LLM wiring is the repo owner's active
   workstream (per `CLAUDE.md`) — this needs their action, not a change made here.
2. **Beat 5 — `CoinGeckoApiClient` sends no `User-Agent` header.** Registered in `Program.cs` as
   a bare `AddHttpClient<IMarketDataApiClient, CoinGeckoApiClient>()` with zero configuration.
   CoinGecko's public API is known to 403 requests without a browser-like `User-Agent` (anti-bot
   measure) — consistent with the instant, consistent 403 seen both runs (not the backoff
   pattern a rate-limit would show). Likely fix: add a `User-Agent` header in the `HttpClient`
   configuration. Small, scoped, but is new backend work beyond this phase's brief — flagging
   for a follow-up task, not fixing inline here.

### Remaining open items

- [x] ~~Confirm Azure OpenAI key/endpoint is configured~~ — confirmed NOT configured; this
      environment runs Ollama only. Needs repo owner action before the real demo.
- [x] ~~Live-verify Beat 4's exact phrase and answer~~ — done; currently wrong both times.
- [x] Two full rehearsals run; cold-boot timing recorded above.
- [ ] Repo owner: wire `AppHost.cs` to Azure OpenAI per D-DEMO-1 (or explicitly decide to accept
      Ollama's quality for this demo — but Beat 4 as observed is not stage-ready either way
      without at least a stronger model).
- [ ] Fix or accept `CoinGeckoApiClient`'s missing `User-Agent` (403 is reproducible, not flaky)
      — otherwise treat Beat 5 as cut per the existing "CoinGecko down → skip" fallback.
- [ ] Once Azure OpenAI is wired, re-run this same rehearsal (this script's exact prompts) once
      more to confirm Beat 2/4 quality before trusting them on stage.
- [ ] Re-run cold-boot timing on the actual demo machine, not this environment.
