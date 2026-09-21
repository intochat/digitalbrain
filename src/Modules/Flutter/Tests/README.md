# Flutter tests

- `Unit`: typed neuron behavior and signals.
- `Integration`: real module process and HTTP contracts, using `BackendOnly()`.
- `E2E`: browser rendering and gestures, using the module's default web host and headless browser.

The browser lane uses the shell's existing `InboxBanner`, which reads the `e2e`
component instances with its authenticated client and sends gestures to the module's
production endpoints. Tests seed these neurons through typed contracts and assert
rendered outcomes. The gallery is static and is not used as evidence of backend/UI
integration. Unit and Integration retain button click state/signal assertions;
the banner exposes no visible click-count feedback.

Pass `?semantics=true` for Flutter's accessible browser DOM. The E2E project retains
the module bundle manifest through `Integration.Tests.props`.
