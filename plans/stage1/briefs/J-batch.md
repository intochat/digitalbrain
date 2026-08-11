# J-batch — janitor sweep + flake hardening   (role: JANITOR)

Report path: `plans/stage1/reports/J-batch.md`

Work through `plans/stage1/janitor-backlog.md` items below IN ORDER, gating after each group.
Characterize-before-delete applies (a search proving zero references IS the characterization
for dead code — paste the searches in the report).

## Deletions
1. **J2**: delete the empty `DigitalBrain.Modules.Salesforce.Contracts` project — verify it is
   truly empty/unreferenced (slnx entry, project refs, composition lists), remove from
   `DigitalBrain.slnx`.
2. **J3**: delete the stale `DigitalBrain__Modules__0..9` env vars from `docker-compose.yml`
   (they name module classes that no longer exist; composition reflects manifests now). Fix any
   other obviously-dead compose references you can PROVE dead (paste proof); touch nothing live.
3. **J1-residue**: remove dead `ChatButtons.OfferLifetime` / `ArmingConnectionId` helpers
   (zero callers — verify again); fix the `'show-time'` example string in
   `DigitalBrain.Mcp/ChatTools.cs` to a neutral example.
4. **J8**: verify `bin/`, `obj/`, `.vs/`, `.codegraph/`, `.grok/` are git-ignored (add missing
   patterns); `git status` must stay clean of build artifacts.

## Streams proof (J5 — prove, do NOT delete yet)
5. Establish by static evidence whether Orleans Streams/PubSub provisioning is consumed
   anywhere: search for stream usage (providers, `GetStreamProvider`, subscriptions) outside
   the Aspire provisioning itself. Write the verdict + evidence into the report (deletion is a
   Stage-2 decision; the ratified rule: outbox never moves to Streams).

## Flake hardening (in-repo quality, not deletion)
6. `ChartVocabularyProofs.EmittedChartPointLandsOnItsBoundChart`: the graph connection lookup
   timed out once at 25s under load. Find the timeout source; widen the margin or make the
   lookup patience load-tolerant WITHOUT weakening the assertion (the route must still be
   proven). Prefer fixture-level patience over per-test hacks.
7. Restart proofs (`RunningTurnSurvivesSiloRestartAndCompletes`, `ExecutionStateSurvivesSiloRestart`,
   liveness/deadline proofs using 15s reminders + 45s `WaitForAsync`): under parallel pressure
   they exceeded patience once. Apply ONE coherent policy: mark the restart/reminder-driven
   proofs as a serial xunit collection (no parallel siblings) and/or raise `WaitForAsync`
   patience for reminder-scale operations. Justify the chosen policy in one paragraph.

## Coverage nits (from J1-GRILL)
8. Pin `Responded.Author` on the sign-in offer path and the default-assistant responder path
   (two small tests).

## Constraints
TDD where a behavior is touched; searches pasted for every deletion; full gate after each
numbered group and at the end (build 0 warnings + full test exe; if a restart proof flakes
during YOUR run, your own hardening from item 7 should fix it — that is the point). No new
packages. No git writes.

## Definition of done
Gate green twice consecutively (prove stability of the hardened suite); every item either done
with proof or explicitly deferred with reason; report lists per-item evidence.
