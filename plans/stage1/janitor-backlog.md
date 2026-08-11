# Stage 1 janitor backlog (living document — orchestrator maintains)

## Open
- J2: delete empty `DigitalBrain.Modules.Salesforce.Contracts` project (empty ≠ consolidation).
- J3: delete stale `docker-compose.yml` `DigitalBrain__Modules__0..9` env vars naming dead classes.
- J4: dual module catalog (AppHost vs `ComposedModules`) — collapse ONLY if purely mechanical, else park to Stage 2.
- J5: prove Orleans Streams/PubSub non-use under aspire telemetry → then remove provisioning
  (and with it the `Aspire.Azure.Storage.Queues` 13.4.6 mixed pin).
- J8: repo hygiene — verify `bin`/`obj`/`.vs` ignored.
- From J1-GRILL (all MINOR):
  - `ChatButtons.OfferLifetime` / `ArmingConnectionId` — dead helpers, zero callers under src/.
  - `DigitalBrain.Mcp/ChatTools.cs:71` — description string still cites 'show-time' example.
  - Pin `Author` on sign-in offer path + default-assistant responder path (coverage nits).
  - Flutter kit fixtures use `show-time` sample ids (Lane C).
  - Docs (`CLAUDE.md`, `UNIFIED-ARCHITECTURE.md`) still describe the deleted demo — docs pass at Stage 1 exit.
- Token-presence slice duplication (`McpTokenPresence`) — evaluate after S1.3 rail lands.

## Done
- J1: `WantsTimeButton`/`ShowTime`/`ButtonActivatedToShowTime` deleted; `Responded.Author` added (W2). GRILL: APPROVE.
- Version unblock: Aspire 26405.3→26376.5 (cache-satisfiable), Storage.Queues→13.4.6; AppHost Sdk aligned.

## Deliberately kept
- Webhook slice (`McpWebhook*` or equivalent) — ratified SDK ingress rail seed (owner amendment §1.18).
- Behavior Studio fixtures — quarantined demo until Stage 3 builds the real host.
