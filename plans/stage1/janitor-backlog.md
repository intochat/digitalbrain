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
- From S1.2-GRILL (deferred MAJORs, orchestrator-ruled):
  - MAJOR-2: grain-level actor enforcement (Chat refuses durable command without stamp) → fold into S1.5 turn pipeline rework.
  - MAJOR-4: bootstrap/user-create atomicity (ETag if-not-exists on marker row) → hardening pass.
  - MAJOR-5: real invitation flow (admin-create IS the ratified MVP; email invites Stage 2).
  - MAJOR-6: two-user isolation through the REAL host must be part of the Stage-1 exit AppHost smoke.
  - MCP `ChatTools`/`ReadChatTranscript` still take bare chatName (principal scoping at MCP edge) → S1.3/S1.5.
  - Orleans dashboard `/orleans` ACL stance → decide at Stage-1 exit.
  - Installation owner constant "dev" remains (one-workspace MVP) → rename/config at Stage-2 consolidation.

## Done
- J1: `WantsTimeButton`/`ShowTime`/`ButtonActivatedToShowTime` deleted; `Responded.Author` added (W2). GRILL: APPROVE.
- Version unblock: Aspire 26405.3→26376.5 (cache-satisfiable), Storage.Queues→13.4.6; AppHost Sdk aligned.

## Deliberately kept
- Webhook slice (`McpWebhook*` or equivalent) — ratified SDK ingress rail seed (owner amendment §1.18).
- Behavior Studio fixtures — quarantined demo until Stage 3 builds the real host.
