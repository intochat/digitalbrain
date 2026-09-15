# Validation record — 2026-09-15

## Checkout and scope

- Project: `E:/intochat/digitalbrain`, branch `v2`, based on `e3deac127`.
- Preserved pre-existing unfinished RecipeNeuron, NeuronInvoker and composition feature work.
- Baseline: 423 backend tests passed, 6 skipped (429 total).
- No production storage rewrite, live provider connection, deployment or git commit performed.

## Final verification

| Verification | Result |
|---|---|
| Full backend suite | **518 passed, 0 failed, 6 skipped; 524 total** |
| Solution build, including Aspire AppHost | **0 warnings, 0 errors** |
| Flutter core Dart tests | **10 passed** |
| Flutter UI tests | **25 passed** |
| Flutter shell tests | **49 passed** |
| `git diff --check` | Passed |

The six backend skips remain the existing opt-in ClickHouse/Docker and coding self-test cases. No new test was skipped. The migration adds 95 passing backend cases to the baseline.

Commands executed:

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --no-restore -p:CodeGraphRefresh=false
dotnet build DigitalBrain.slnx -p:CodeGraphRefresh=false
# From src/Modules/UI/Flutter/core:
dart test --reporter compact
# From src/Modules/UI/Flutter/ui and then shell:
flutter test --no-pub --reporter compact
```

## Behavior evidence

- Actual workspace ConversationalAgent/native tool registration: catalog → save → start → read → list; old specialist tools retained.
- Typed Twitter receipt → filter → mapper → existing chart action; ignored posts and duplicate provider receipts do not add chart points.
- Internal-event graph works after a cold silo restart against the same file-backed storage.
- Definitions, deterministic node identities, ownership claims and additive connection leases survive restart. Stop preserves shared/manual connections.
- Incompatible schemas, unknown configuration, invalid mappings, lifecycle overlaps and foreign ownership cannot silently activate a bad graph.
- Structured decisions validate JSON output and reject function calls/function-invocation middleware.
- Stable action identities prevent duplicate processing after restart. Lost terminal records, lost responses and missing dispatch produce inspectable uncertainty with later inputs retained.
- Busy before admission retries only when the target journal proves no command attempt; Busy after an attempt remains uncertain.

These tests use scripted model responses, the existing scripted content screen, simulated provider receipts, and the actual durable runtime. They verify execution and recovery, not the planning quality of an arbitrary live model. Production content screening remains enabled.

## Deployment prerequisites and limits

The AppHost and container module manifests include Twitter. No live X API subscription, webhook service or credentials were configured. A trusted provider adapter must feed the typed receipt boundary for real posts. Existing authenticated describe/call surfaces can simulate receipts. See [TWITTER.md](TWITTER.md).

No live AppHost session or production data migration was run. Existing HTTP and client behavior is covered by the backend and Flutter suites. See [EXAMPLES.md](EXAMPLES.md) for runnable demonstrations and [README.md](README.md) for supported schema/composition and recovery limits.

## Telegram addition — final verification

- Full backend regression: **542 passed, 0 failed, 6 existing opt-in skips** (548 total).
- Final focused Telegram rerun after clarification normalization: **14/14 passed**.
- Solution build including AppHost: **0 warnings, 0 errors**.
- Flutter core **10/10**, shared UI **29/29**, shell **49/49**, Telegram Mini App **5/5** passed (93 client tests).
- Flutter Mini App analysis clean and release web build succeeded. The build reports a shared dependency Cupertino font warning; the Mini App uses Material icons.
- Confirmed `index.html`, Flutter bootstrap, JS and assets in the silo `telegram-app` build output, with `/telegram/app/` base URL.
- Auth tests cover stale/forged signatures, wrong webhook secret, malformed updates, query/body attempts to select another user, and scoped dismissal. Recovery tests cover independent timers, cancellation, cold restart, deduplication, delayed absolute deadlines and domain refusal followed by a valid request.
- No live bot registration, real model/provider requests, deployment, or commit performed. The release workflow change was inspected and its constituent Flutter/.NET build commands ran locally; the hosted release pipeline was not invoked.

See [Telegram design and operation](TELEGRAM.md) and [implementation plan](TELEGRAM-PLAN.md).
