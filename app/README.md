# DigitalBrain — Flutter client

The web/mobile client for **DigitalBrain / NeuroOS**. The web build is published to Azure Static
Web Apps and talks to the backend API at **`https://api.digitalbrain.tech`** (the kernel runtime,
hosted on Azure Container Apps).

## Architecture

```
www.digitalbrain.tech        →  this app (Flutter web, Azure Static Web Apps)
digitalbrain.tech            →  forwarded to www.digitalbrain.tech at the registrar
api.digitalbrain.tech        →  DigitalBrain kernel (Orleans/.NET on Azure Container Apps)
```

The frontend is a static bundle; all dynamic work (LLM, neurons, journals) happens in the backend.

## Repo layout

| Path | What |
|------|------|
| `lib/`, `web/`, `assets/` | the Flutter client (`digitalbrain_flutter`) |
| `packages/digital_brain_sdk_flutter/` | vendored self-instrumenting perf SDK the app depends on (`path:` dependency) |
| `.github/workflows/deploy.yml` | release-gated Azure deploy for backend images and Flutter web |

## Run locally

```sh
flutter pub get
flutter run -d chrome           # or any connected device
```

## Build the web bundle

```sh
flutter build web --release --base-href "/"
```

## Deploy

Publishing a GitHub Release runs `.github/workflows/deploy.yml`, which builds the Flutter web bundle,
uploads it to Azure Static Web Apps (`digitalbrain-web-prod`), and compiles the client with
`KERNEL_ENDPOINT=https://api.digitalbrain.tech`. Push/PR CI only validates the build and tests.

## Related repos

- [`digitalbraintech/framework`](https://github.com/digitalbraintech/framework) — backend runtime + Azure deploy
- [`digitalbraintech/sdk`](https://github.com/digitalbraintech/sdk) — DigitalBrain UI kit
- [`digitalbraintech/awesome`](https://github.com/digitalbraintech/awesome) — INO-described example experiences
