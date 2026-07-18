# DigitalBrain (self-improving minimal prototype)

**Aspire = central local orchestrator. Neurons + synapses only. Flutter UI kit shell. Closed authoring 1.0/2.0. Speed + TDD from day 0.**

`aspire run` (core fast; flutter-web explicit) -> dashboard or `aspire resource flutter-web start` (needs Flutter SDK on host) -> open http://localhost:5801 -> press ▶ DEMO
- ClientTap -> kernel emits UiSurface (synapse-log + "Demo Executed" card) -> visible in server log + browser card.
- Maps button pins simple list/widget (brain-graph participation + grain emit).
- Headless: `aspire resource <kernel> fire-demo` (or /fire-demo http) for CI/test without browser.
- Marketplace always present (bundles/N+1 core law).
- High-sev gate + build green. Pruned per 5 steps.

## Quick (mcp test)
```pwsh
cd self-improving
aspire run
# explicit flutter (when SDK present):
aspire resource flutter-web start
# browser to 5801, press DEMO -> see log + card
aspire ps
aspire describe
aspire resource <kernel-name> fire-demo   # headless
aspire resource <kernel-name> rebuild-silo
dotnet test src/DigitalBrain.Os.Tests/DigitalBrain.Os.Tests.csproj --filter "DistributionDynamicHandlers"
```

Pruned per 5 steps. Core Law. Ready for digital brain use.

Canonical clean reboot (.NET 11 + Orleans 10.2.0 runtime/providers + Journaling alpha adapter + Aspire 13.4/13.5 + Hex1b 0.165.0-alpha + Reqnroll).

Everything is a neuron (INeuron) or synapse (immutable typed message). Marketplace installs behavior as bundles; N+1 growth on broadcast is the Core Law proof (DistributionDynamicHandlers high-sev gate).

## Quickstart (aspire run headline per D5; ino.cs keeps brand)
```pwsh
cd final
aspire run
# or: dotnet run ino.cs   (equivalent where Aspire CLI bundle present; single-file AppHost)
```
- Uses ino.cs (single-file Aspire AppHost at root) + shared AddDefaultDigitalBrainTopology.
- Dashboard + root/example kernels + flutter resource come up; TUI (`dotnet run --project src/DigitalBrain.Clients.Console`) connects to cluster.
- /pack /publish /install /market smoke work; ino chat, discovery, bundles.
- For LAN: set DIGITALBRAIN_ADVERTISED_IP before aspire run; firewall gateway 30000.

## Flagship two-machine + discovery + secure install (copy-paste)
Machine A (host):
```pwsh
# set your LAN IP for beacon/advertise (others discover you)
$env:DIGITALBRAIN_ADVERTISED_IP = "192.168.1.42"
aspire run   # or dotnet run ino.cs
# TUI A (root): chat a bit; /pack my-rule "daily standup"; /publish my-rule
# /market (lists local); beacon is broadcasting digitalbrain-market root <A-ip>:30000
```

Machine B (or second console on A with --world):
```pwsh
# no ADVERTISED needed for consumer (pulls via peer) 

## Flagship demo #3 (ino orientation + gmail install flow, <90s, OS6 per plan §3.8)
aspire run (or dotnet run ino.cs)
# login (root)
# workspace appears (tasks + weather pinned widgets from os/ + headers)
# open Marketplace (or ask ino "show marketplace")
# Install "gmail-last-senders" (requires google-auth surface + button)
# Install google-auth → Connect Google (stub or real loopback /oauth)
# Allow grant surface for SaveFileRequest (journaled GrantDecision)
# N+1 (new handler participates)
# Ask ino "who emailed me lately?" or "list recent senders"
# Sender card lands in widgets region (UiSurface)
# Tap "Save to file" → file written, path surfaced (grant-gated SaveFileRequest)
# (manual real-Google: 5-min console project + consent + credentials + gmail.readonly + test user; construct accounts URL with your client_id + kernel redirect; code has loopback + real template)
# Traceable to brain.ino seeds + os/gmail.ino + manifest headers + grant + GmailNeuron.
```

```

Matches current (beacon E8, /market scan, token floor E9, rule capsules/executable ino E6/E10, N+1 preserved, no restart).

See docs/USER-FLOWS.md for full flows; docs/ROADMAP.md for GlobalBrain next; docs/REFACTORING-STAGES.md for traceability.

High-sev gate (DistributionDynamicHandlers) must be 0 failures. Full run-ci green at land. One commit per session.

## E10/E11/M5 close (this session)
- Real Aspire.Hosting.Testing two-kernel E2E (publish A, install B, surface delivery).
- Strengthened high-sev BDD for discovery scan + secure peer install.
- Flutter widget test parity for buildFromUiWidget (marketplace + rule surface roundtrip; Render format unchanged).
- E7 Aspire typed command (publish-experience on kernel resource).
- GlobalBrain Phase 5 skeleton (GlobalPeer ser + persist in MarketplaceState + sync/telemetry in MarketplaceNeuron; still IMarketplace; roundtrip probe).
- E11 productize: start/launcher first-run (username + auto guidance + firewall note); precise README quickstart; minimal wizard reusing existing synapses (Login etc); works for TUI + future Flutter.
- All new ser types (GlobalPeer etc) have collector + probe roundtrips.
- Gate 0 failures (existing + new/strengthened); no regress fork/confirm/N+1/signed/executable-ino/discovery/security.

See docs/M5-COMPLETION-PLAN.md for the 5 Steps plan + exact contracts/DoD/landmines/deletions.

## Dev
- Fast: `aspire run` (or `dotnet run ino.cs`)
- High-sev gate: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --filter "FullyQualifiedName~DistributionDynamicHandlers" --logger "console;verbosity=minimal"`
- Full: `.\run-ci.ps1`
- Aspire (hosting): `aspire --version`; `cd src/DigitalBrain.AppHost; dotnet build`; `aspire run` (or ps/logs/stop).
- After changes: build + gate + AppHost build + aspire smoke.

Latest packages central in Directory.Packages.props. Self-explanatory names only (no boilerplate ///). Relative paths inside final/. Context7/official docs for all APIs before code.

One commit. Tree clean except it. Docs updated (DELETED lists deletions with owner; USER-FLOWS/ROADMAP/PRODUCT-SPEC/REFACTORING-STAGES marked).

## License / etc
See VISION.md + docs/.

(Updated for M5 "It ships" completion + GlobalBrain prep per session prompt + plan.)--- append quickstart ---

## simulation.cs (SIM2+)
dotnet run simulation.cs -- --list
dotnet run simulation.cs -- "Distribution" --ci   # reproduces high-sev gate (0f core via mapped MTP filter + CTRF)
dotnet run simulation.cs -- "tag:Journals"
dotnet run simulation.cs -- "ino:executable-standup"
# --ui watch: filtered + real AppHost/flutter web + Playwright headed (or OS URL fallback + print); screenshots; wait Ctrl+C. Skips @Ui gracefully if no Flutter/playwright (env gap recorded).
# Reports/artifacts: pa-files/simulations/{runId}/ (SimulationReport.json + CTRF + pngs)
# run-ci now delegates through it for green (proper filters, one owner of summary/exit).

## Deploy (marketplace + kernel private + flutter)
- Marketplace "has to be there": always seeded (os/marketplace.ino + neuron) for bundles/N+1. Use for private deploys.
- Kernel + marketplace as private software: use DeploymentKit (Pulumi Azure facade from https://github.com/rosextechnology/deploymentKit) for Container Apps / Redis / KeyVault etc. (fluent settings, drift, green-blue).
- Flutter:
  - Web: `flutter build web` from Clients.Flutter → GitHub Pages (or custom domain).
  - TG miniapp: host the web build at https url (TG WebApp supports any https Flutter web).
  - Windows + other OS: `flutter build windows/macos/linux`; distribute privately (releases, not public Pages).
- Marketplace stays the source of truth for installable experiences across deploys.
- Local: aspire run + explicit flutter-web; prod kernels pull from private marketplace peer.

(5 steps applied: questioned auto-flutter + SKIP (dumb for core speed), deleted dead test refs + skip logic, simplified to WithExplicitStart + /fire-demo, accelerated (fast run + mcp headless), automate last.)
