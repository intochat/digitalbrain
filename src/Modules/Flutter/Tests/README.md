# Flutter tests

- `Unit`: typed neuron behavior and signals, with the brain in the test process.
- `E2E`: the module in a real process. `*HttpFacts` use `BackendOnly()` and assert transport and HTTP
  contracts; `*WebFacts` use `RunWebApp()`, which starts the Flutter web frontend and a headless
  browser. Both kinds sit together under the component they cover.

The browser lane uses the shell's existing `InboxBanner`, which reads the `e2e`
component instances with its authenticated client and sends gestures to the module's
production endpoints. Tests seed these neurons through typed contracts and assert
rendered outcomes. The gallery is static and is not used as evidence of backend/UI
integration. `*HttpFacts` retain button click state and signal assertions;
the banner exposes no visible click-count feedback.

Pass `?semantics=true` for Flutter's accessible browser DOM.
