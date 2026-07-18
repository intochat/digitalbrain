# DigitalBrain (final)

Canonical clean reboot (.NET 11 + Orleans 10.2.0 runtime/providers + Journaling alpha adapter + Aspire 13.4/13.5 + Hex1b 0.165.0-alpha + Reqnroll).

Everything is a neuron (INeuron) or synapse (immutable typed message). Marketplace installs behavior as bundles; N+1 growth on broadcast is the Core Law proof (DistributionDynamicHandlers high-sev gate).

## Quickstart (headline E2E per 5 steps: aspire run + explicit flutter-web + press DEMO)
```pwsh
cd final
aspire run
# flutter-web added as executable resource (port 5801) with ExplicitStart by default (no SKIP in normal dev)
```
- Dashboard comes up.
- Explicit start the flutter UI: use Aspire dashboard resource commands or MCP `execute_resource_command("flutter-web", "start")` (or `aspire` resource start flows).
- Open browser to http://localhost:5801 (or the surfaced endpoint).
- Press DEMO (or nav to Marketplace / design surfaces in the rendered shell or demo fallback) → live surfaces update (log + "Demo Executed" style card from UiSurface / kernel reaction).
- TUI alternative: `dotnet run --project src/DigitalBrain.Clients.Console`
- Marketplace is always present via seeds (marketplace neuron + bundles); install flows trigger N+1.

Full flow uses the neuron/synapse model end to end; flutter-web requires Flutter SDK on host for web/windows dev runs. CI uses SKIP_FLUTTER_RESOURCE to keep gates fast without SDK.

Or single-file: `dotnet run ino.cs` (same topology).

## Deployment (marketplace + kernel private + flutter surfaces; follow 5 steps order)

**1. Requirements questioned (traceable):** Marketplace neuron must be present for bundle install/N+1 proof (core law) in private deploys. Kernel hosts it. Flutter web is the public surface (demo + TG miniapp). Private software uses controlled infra for marketplace+experiences.

**2. Deleted ruthlessly:** No "just in case" full multi-target matrix. No Pulumi bloat in main tree until private prod needed. Public demo stays zero-infra (GH Pages). No new private marketplace grain - reuses existing via seeds (marketplace.yaml + brain.yaml).

**3. Simplified:** 
- Flutter web → static `flutter build web` → GitHub Pages (or any static host / TG web_app URL). Same build serves desktop windows via separate `flutter build windows` + releases.
- Private marketplace/kernel → containerized kernel (DigitalBrain.Kernel or host) + seeds for marketplace. Use Azure Container Apps (aspire deploy or custom) + supporting (Redis/Storage for clustering, KV).
- DeploymentKit (https://github.com/rosextechnology/DeploymentKit) fits private Azure: Pulumi facade with ACA components, drift recovery, green-blue, validation. Call InfrastructureDeployer with settings for container app(s) running the kernel image (publish via `dotnet publish` + docker or ACA direct). Marketplace "is there" because os-on-yaml/marketplace seeds + boot include it; installs work immediately on start.

**4+5. Accelerate then automate last:** Manual `aspire run` + explicit flutter start + browser DEMO first. CI: GH Action for flutter-web to Pages (build web, deploy). Private deploys via kit script or pipeline only after E2E headline green. Use mcp resource start / send for headless demo taps in tests/CI.

Example private cluster layout already in multirepo/private-cluster (thin AppHost + marketplace-focused brain.yaml). For full IaC, reference DeploymentKit (net11, Pulumi 3.x + AzureNative) in a separate deploy console; configure ContainerApp for the kernel with env for world/seeds, ingress for gateway (30000) + gRPC (8080 for surfaces), health.

Flutter TG miniapp: host the Pages build at public URL; Telegram bot sends web_app button with initData. Flutter can read platform env for TG mode (minimal chrome if desired).

Edge: host Flutter SDK for web/windows dev; GPU not required for web; file locks avoided by mcp stop before re-run; use explicit ports. Latest packages central.

See also aspire docs for `aspire deploy` to ACA (complements kit for pure Aspire paths). Marketplace contract bundles enable public surface sharing without full private bits.

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
- High-sev gate: `dotnet test src/DigitalBrain.Os.Tests/DigitalBrain.Os.Tests.csproj --filter "FullyQualifiedName~DistributionDynamicHandlers" --logger "console;verbosity=minimal"`
- Full: `.\run-ci.ps1`
- Aspire (hosting): `aspire --version`; `cd src/DigitalBrain.AppHost; dotnet build`; `aspire run` (or ps/logs/stop).
- After changes: build + gate + AppHost build + aspire smoke.
- Marketplace two-client test (asap): `aspire run` → dashboard → start "console-market-a" + "console-market-b" (or `dotnet run --project src/DigitalBrain.Clients.Console -- --brain-key market-a` twice in two terminals). In a: pack+publish an id; in b: Marketplace tab → "install id> local listed". Uses separate brain keys + shared global marketplace.

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
