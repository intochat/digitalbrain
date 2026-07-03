# DigitalBrain — Flutter client

The web/mobile client for **DigitalBrain / NeuroOS**. The web build is published to GitHub
Pages at **[digitalbrain.tech](https://digitalbrain.tech)** and talks to the backend API at
**`https://api.digitalbrain.tech`** (the [`digitalbraintech/framework`](https://github.com/digitalbraintech/framework)
runtime, hosted on Azure Container Apps).

## Architecture

```
digitalbrain.tech            →  this app (Flutter web, GitHub Pages, Fastly CDN)
api.digitalbrain.tech        →  digitalbraintech/framework (Orleans/.NET on Azure Container Apps)
```

The frontend is a static bundle; all dynamic work (LLM, neurons, journals) happens in the backend.

## Repo layout

| Path | What |
|------|------|
| `lib/`, `web/`, `assets/` | the Flutter client (`digitalbrain_flutter`) |
| `packages/digital_brain_sdk_flutter/` | vendored self-instrumenting perf SDK the app depends on (`path:` dependency) |
| `.github/workflows/deploy-flutter-web.yml` | builds web + deploys to GitHub Pages |
| `web/CNAME` | binds the Pages site to the apex domain `digitalbrain.tech` |

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

Pushing to `main`/`master` triggers **`Deploy Flutter Web`**, which builds the release web bundle
and publishes it to GitHub Pages. One-time setup: **Settings → Pages → Source: GitHub Actions**, and
verify the `digitalbrain.tech` custom domain (apex `A` records → GitHub Pages + `www` CNAME →
`digitalbraintech.github.io`). The backend's `api.` host is configured separately in the
[`framework`](https://github.com/digitalbraintech/framework) repo's deploy.

## Related repos

- [`digitalbraintech/framework`](https://github.com/digitalbraintech/framework) — backend runtime + Azure deploy
- [`digitalbraintech/sdk`](https://github.com/digitalbraintech/sdk) — DigitalBrain UI kit
- [`digitalbraintech/awesome`](https://github.com/digitalbraintech/awesome) — INO-described example experiences
