# Module test migration

Implemented Task 2 in the existing `archv2` checkout, without commits.

## Organization

- Flutter `Tests.Unit` → `Tests/Unit`, same project/assembly name; feature namespaces updated.
- Flutter `Tests.Integration` → `Tests/Integration`, same project/assembly name; feature namespaces updated and host calls use `BackendOnly()`.
- Supabase `Tests` → `Tests/Unit`, same project/assembly name; feature namespaces updated.
- New `Flutter/Tests/E2E/DigitalBrain.Modules.Flutter.Tests.E2E.csproj` owns Button, Expander, and Rendering browser coverage formerly in IntoChat.
- Relative project references/imports adjusted; E2E imports `Integration.Tests.props` and its generated `module-bundle.json` was inspected.

## Live UI coverage

The existing shell's `InboxBanner` hosts live `e2e` component instances above its workspace/library view. Tests reuse that production surface and its authenticated HTTP adapters. No production code changes are required. An initially added inspector was removed after locating this existing live surface.

E2E setup uses typed neurons. Assertions check rendered labels, expanded state, and chart/browser/video content, leaving signals and protocol assertions in existing Unit/Integration tests. The banner exposes no visible button-click feedback, so button click state/signals remain in Unit/Integration and its gesture wiring is covered by a widget test. Browser facts share one xUnit collection because the Flutter hosts use the same package build directory.

Added shell widget coverage for next/previous page and ascending/descending sort gestures. Existing clear-filter widget coverage verifies the server response is rendered. These focused component checks supplement the product's real-backend table browser scenario.

## Verification

- Flutter Unit: **34 passed**.
- Supabase Unit: **9 passed**.
- Flutter Integration: **25 passed**, real module host, 44 seconds.
- Flutter E2E project: Debug and Release builds passed, 0 warnings/errors; discovery finds all 3 feature facts.
- Real browser Button and Expander scenarios: **2 passed**. Rendering reached the correct backend content but its group-role locator failed because the passive chart exports a text span. The trace DOM confirms one leaf span with `E2E BTC chart\nE2E BTC chart`, one with the browser title/URI, and one with the video URL. Changed chart to a substring text locator, matching those unique leaf nodes; targeted Release rerun delegated to the root verification queue.
- Full shell widget suite: **14 passed**, including `inbox_banner_test`, `table_navigation_test`, and existing clear-filter rendering coverage.
- UI kit widget suite: **4 passed**.
- Static analysis of the two added shell test files: **no issues**.
- First browser attempt exposed the old module-runner package-path resolution. Framework owner fixed the path and added a regression test; browser project rebuilt for a rerun.

Context7 documentation lookup returned its monthly quota error. Current Flutter official documentation was consulted for routing; final implementation reuses the existing shell route.

Final rendering verification: 1 passed, 0 failed, 1m26s. Trace DOM established one unique text span for each rendered chart, browser and video label. Evidence: artifacts/testing-redesign/module-rendering-rerun.log.
