# Scenario 49: Behavior marketplace install activates new handlers N+1

## User intent
The owner browses a behavior marketplace, installs “Travel disruption assistant” (hears flight emails + calendar, proposes rebooks). Immediately, without redeploying the product, new handlers participate in the brain; existing chats keep working; the new behavior appears in topology and capability lists as N+1.

## Trigger
Marketplace UI `InstallBehavior(packageId)` → download → compile/activate.

## Imagined modules
- Marketplace catalog (remote packages)
- BehaviorHost install pipeline
- Gmail + Calendar (existing)
- TravelDisruption behavior (new)
- Capability catalog for assistant tools
- Ui marketplace + topology

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Marketplace / shell | Install UX |
| PackageInstaller / host | Fetch, verify signature, compile |
| TravelDisruption / default | New listeners/handlers |
| GmailIngress / inbox | Existing EmailReceived source |
| Calendar / personal | Existing |
| Assistant / desk | Sees new capabilities after activate |
| Topology / shell | Shows new neuron kind |

## Synapse choreography
1. `InstallBehaviorRequested` → PackageInstaller verifies signature/permissions → `PackageVerified`.
2. Compile → `BehaviorPackageCompiled`; failure ends with UI error, no partial handlers.
3. `BehaviorPackageActivated` broadcast: catalog registers TravelDisruption kinds and any new Ask answerers.
4. Capability catalog Emits `CapabilitiesChanged`; Assistant next turn may select travel tools.
5. Next matching EmailReceived is also heard by TravelDisruption (N+1 listener); older listeners still hear (broadcast).
6. TravelDisruption Emits `TravelDisruptionDetected`, `RebookOptionsProposed`, UiSurface.
7. Uninstall → Disconnect/deactivate; in-flight turns drain; catalog removes answerers carefully to avoid leaving zero answerers for still-needed kinds.

## Orleans / Core surface exercised
Module catalog dynamic add; grain activation of new kinds; DurableGrain journals; Connect topology; grain call filters (package permissions); grain versioning of host; outbox; request context.

## Rich experience
Marketplace product page + permissions list (email read, calendar write); install progress; topology animation adding node; sample “test with fixture email” button; rating/feedback.

## Failure / adversarial cases
- Malicious package claiming to answer core Asks → activation validation + permission manifest.
- Install adds second answerer for existing Ask → activate fails loud.
- Partial activate after crash → installer epoch; rehydrate or rollback from journal.
- Uninstall while handler mid-turn → finish turn; then unbind.

## Capability claim
DigitalBrain can grow its live handler graph from a marketplace install with catalog-safe activation—extending the nervous system, not pasting text into a system prompt.
