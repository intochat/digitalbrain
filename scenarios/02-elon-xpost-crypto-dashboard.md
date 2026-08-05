# Scenario 02: Elon X post → six-coin crypto dashboard

## User intent
When Elon Musk posts on X about markets, crypto, or a named asset, the owner wants a live dashboard to append chart points and short descriptions for six tracked coins (BTC, ETH, SOL, DOGE, XRP, ADA), tying price reaction windows to the post.

## Trigger
External event: X module observes a new post from a watched account (`elonmusk`) matching keyword/topic filters.

## Imagined modules
- X/Twitter (account watch, post body, engagement counters)
- CryptoMarket (spot prices, OHLCV windows)
- CryptoDashboard (tracked set, chart series, annotations)
- NLP/Topic (post → asset relevance scores)
- Chat (optional alert blurb)
- Shell UI (dashboard scene with multi-series chart)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| xwatch/elonmusk | Observe posts; emit ambient social facts |
| topicrouter/markets | Score post relevance to crypto/assets |
| cryptomarket/spot | Answer price/window asks for symbol sets |
| cryptodashboard/owner-six | Hold tracked coins; append points + annotations |
| chat/alerts | Optional human-facing alert |
| shell/primary | Bind dashboard widgets to series updates |

## Synapse choreography
1. `xwatch/elonmusk` **broadcasts** `XPostObserved` (postId, text, mediaUrls, createdAt, metrics).
2. `topicrouter/markets` hears it, **broadcasts** `MarketSignalClassified` (topics[], assetHints[], relevance).
3. If relevance ≥ threshold: router **directs** `DashboardAnnotateAsked` → `cryptodashboard/owner-six` (postRef, hints).
4. Dashboard **directs** `SpotSnapshotAsked` → `cryptomarket/spot` for symbols `[BTC,ETH,SOL,DOGE,XRP,ADA]`.
5. Market answers directed `SpotSnapshotAnswered` (per-symbol price, 5m/1h delta, volume).
6. Dashboard **broadcasts** six `ChartPointAppended` facts (symbol, t, price, delta, seriesId=owner-six) — ambient for any chart renderer.
7. Dashboard **broadcasts** `ChartAnnotationAdded` (postId, excerpt, linkedSymbols[], description synthesized from post+moves).
8. Dashboard **broadcasts** `CryptoDashboardUpdated` (revision, coinSummaries[6]).
9. Shell widgets hearing `ChartPointAppended` / `CryptoDashboardUpdated` refresh series without a chat turn.
10. Optional: chat **broadcasts** `AssistantResponded`-style alert card only if owner enabled `NotifyOnElonCrypto`.

## Orleans / Core surface exercised
Streams (explicit subscription for high-frequency `ChartPointAppended`); DurableGrain journals on dashboard; serialized turns so point append order per symbol is FIFO; timers for post-event sampling windows (T+1m, T+5m, T+15m re-snapshot); stateless workers optional for topic classification; pub-sub for ambient social facts; outbox durability so chart points survive silo blips.

## Rich experience
Multi-pane shell: left = post card with text/media; center = six-series chart with annotation marker at post time; right = table of coin, price, Δ5m, Δ1h, one-line description. Tapping a coin filters the annotation list.

## Failure / adversarial cases
- Burst posts: watermark dedup on `XPostObserved`; dashboard must not double-append identical (postId,symbol,window) points.
- Partial market API failure: journal `SpotSnapshotPartial`; append points only for succeeded symbols; mark others `ChartPointDeferred`.
- Topic false positive: low relevance must not spam chart — threshold + owner mute list as memory facts.
- Stream subscriber crash: re-subscribe from last journaled dashboard revision, not from volatile UI state.
- Description model hallucination: description field is module-produced text labeled as such; journal stores post excerpt + numeric deltas as facts, not the prose as causal truth if policy forbids.

## Capability claim
A social observation becomes a multi-asset, multi-window dashboard mutation as first-class synapses — live charts update from the nervous system, not from a chat model re-scraping Twitter on every question.
