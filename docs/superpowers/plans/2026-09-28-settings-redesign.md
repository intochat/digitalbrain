# Settings redesign implementation

Approved direction: unify Settings into General, Connections, Models, Advanced, plus a visible UI Kit showcasing Flutter neuron components for composing apps. Reference: docs/research/2026-09-28-settings-connections.md.

Tasks:
- Backend configured model catalog and validated per-turn profile selection; preserve automatic default and run pinning.
- Connections: unified service list with truthful configured/verified states, working authorization start and existing credential management.
- UI Kit: visible browsable Flutter neuron catalog with previews and composition guidance, reused from existing gallery where possible.
- Shell: one settings route, general appearance/name controls, remove unused role/static assistant brochure, Models default and per-conversation composer picker, Advanced storage recovery. Preserve previous persisted data.
- Tests: scoped/auth model validation, connection status/actions, settings navigation/persistence, narrow layout and UI Kit; analysis/build and final review.

Rulings: user authorization covers implementation of the recommended option plus UI Kit. Existing checkout is clean except the related research note and is reused. No new credentials or external accounts will be changed during verification. Configured model profiles are exposed without secrets; provider configuration remains operator-managed. Model default is a device preference used for new conversations, explicit per-conversation selection persists with that conversation; automatic follows server default. UI Kit is user-visible, overriding the earlier suggestion to hide the gallery.

Progress: unified shell Settings and first-class UI Kit implemented; General uses device preferences, Models default applies to newly created conversations and composer selection persists per conversation. Prior route aliases remain accepted. All 88 shell tests passed, then an additional unavailable-model recovery regression passed in the 7-test focused suite. UI Kit has all 29 registered neuron kinds plus presentation examples, search, preview and composition definitions; 33 gallery tests passed. Web build succeeded; General/UI Kit rendered and visually inspected.

Review corrections: completed runs replay without requiring a still-configured model; new connection secrets include registry identity to prevent cross-workspace collisions; both Settings request callbacks capture the same workspace ID; connection requests ignore stale scope responses. Gmail browser OAuth is not implemented by the deployment and GitHub requires repository setup, so both are honestly unavailable in generic Connections rather than broken links. Salesforce authorization is scoped. Package account selection compatibility is being verified.

Completed: package account discovery/validation and Settings now share the scoped registry plus provably owned legacy records. Raw workspace-only legacy records without attributable ownership remain untouched. Final scoped review found no blocker. Backend validation: model unit tests 10 passed; actual HTTP model auth/catalog/invalid-selection E2E 1 passed; connection facts 6 passed; kernel connector facts 6 passed. Gallery suite rerun: 33 passed. Broader application suite retains four unrelated missing-repository-file failures; pre-existing UI registry-count test still expects 30 instead of 29. No external account credentials changed, no app restart, and no commit in this implementation turn.
