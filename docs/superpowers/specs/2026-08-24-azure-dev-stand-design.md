# Azure dev stand: kernel on Container Apps + gated shell

Date: 2026-08-24. Status: ratified in interview; implementation pending.

## Decisions (ratified)

| Decision | Choice |
| --- | --- |
| Topology | Shell stays on the existing Static Web App (purple-sky); kernel runs on Azure Container Apps; kernel gains CORS for the SWA origin |
| Auth | Basic auth everywhere: kernel middleware validates `Authorization: Basic` against env creds; Flutter login screen; token-free by design (dev stand, replaced by real auth later) |
| AI | OpenAI API key as a Container App secret; `Default__Model=IGpt54Nano`, `Default__Embedding=ITextEmbedding3Small` (embedding pin is mandatory in cloud — the code default is local Ollama and throws). No Azure OpenAI: the code has no such provider |
| Vector memory | Qdrant Cloud free tier (external), via `ConnectionStrings__memory-qdrant` |
| Region / scale | `westeurope`; min 1 / max 1 replicas (always-on; the silo has no clustering ports, so >1 replica is forbidden) |

## Inputs the owner provides

1. `OPENAI_API_KEY` — an openai.com key (nano-tier spend expected).
2. `QDRANT_ENDPOINT` + `QDRANT_API_KEY` — from a cluster at cloud.qdrant.io (gRPC endpoint, port 6334).
3. `DEV_USERNAME` + `DEV_PASSWORD` — the single test user's credentials.

## Section A — Azure resources (az CLI)

Names: `rg-digitalbrain-dev` / `stdigitalbraindev` / `cae-digitalbrain-dev` / `ca-digitalbrain-kernel`. Run in order; `$VARS` are the inputs above plus derived values.

```bash
az login
az extension add --name containerapp --upgrade
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.OperationalInsights --wait

# 1. Resource group
az group create --name rg-digitalbrain-dev --location westeurope

# 2. Storage account (Tables: clustering/reminders; Blobs: journal/grainstate/kit-images)
az storage account create \
  --name stdigitalbraindev \
  --resource-group rg-digitalbrain-dev \
  --location westeurope \
  --sku Standard_LRS \
  --kind StorageV2 \
  --min-tls-version TLS1_2 \
  --allow-blob-public-access false

STORAGE_CONN=$(az storage account show-connection-string \
  --name stdigitalbraindev --resource-group rg-digitalbrain-dev \
  --query connectionString --output tsv)

# 3. Container Apps environment (Log Analytics auto-provisioned)
az containerapp env create \
  --name cae-digitalbrain-dev \
  --resource-group rg-digitalbrain-dev \
  --location westeurope

# 4. Kernel container app
QDRANT_CONN="Endpoint=${QDRANT_ENDPOINT};Key=${QDRANT_API_KEY}"
SWA_ORIGIN="https://purple-sky-0f80f110f.7.azurestaticapps.net"

az containerapp create \
  --name ca-digitalbrain-kernel \
  --resource-group rg-digitalbrain-dev \
  --environment cae-digitalbrain-dev \
  --image docker.io/vhorbachov/digitalbrain-kernel:v0.1.20 \
  --ingress external --target-port 8080 \
  --min-replicas 1 --max-replicas 1 \
  --cpu 0.5 --memory 1.0Gi \
  --secrets \
      storage-conn="$STORAGE_CONN" \
      openai-key="$OPENAI_API_KEY" \
      qdrant-conn="$QDRANT_CONN" \
      auth-username="$DEV_USERNAME" \
      auth-password="$DEV_PASSWORD" \
  --env-vars \
      ConnectionStrings__clustering=secretref:storage-conn \
      ConnectionStrings__reminders=secretref:storage-conn \
      ConnectionStrings__journal=secretref:storage-conn \
      ConnectionStrings__grainstate=secretref:storage-conn \
      Orleans__ClusterId=digitalbrain \
      Orleans__ServiceId=digitalbrain \
      Orleans__Clustering__ProviderType=AzureTableStorage \
      Orleans__Clustering__ServiceKey=clustering \
      Orleans__Reminders__ProviderType=AzureTableStorage \
      Orleans__Reminders__ServiceKey=reminders \
      Orleans__GrainStorage__Default__ProviderType=AzureBlobStorage \
      Orleans__GrainStorage__Default__ServiceKey=grainstate \
      DigitalBrain__AI__OpenAI__ApiKey=secretref:openai-key \
      DigitalBrain__AI__Default__Model=IGpt54Nano \
      DigitalBrain__AI__Default__Embedding=ITextEmbedding3Small \
      DigitalBrain__Memory__Provider=Qdrant \
      ConnectionStrings__memory-qdrant=secretref:qdrant-conn \
      DigitalBrain__Auth__Username=secretref:auth-username \
      DigitalBrain__Auth__Password=secretref:auth-password \
      DigitalBrain__Cors__AllowedOrigin="$SWA_ORIGIN" \
      ASPNETCORE_ENVIRONMENT=Production

# 5. Kernel public URL (needed for the SWA build define and smoke tests)
KERNEL_FQDN=$(az containerapp show \
  --name ca-digitalbrain-kernel --resource-group rg-digitalbrain-dev \
  --query properties.configuration.ingress.fqdn --output tsv)
echo "https://$KERNEL_FQDN"

# 6. Health probes (CLI has no probe flags; round-trip the YAML)
az containerapp show --name ca-digitalbrain-kernel \
  --resource-group rg-digitalbrain-dev --output yaml > kernel-app.yaml
# In kernel-app.yaml, under properties.template.containers[0], add:
#   probes:
#     - type: Startup
#       httpGet: { path: /health, port: 8080 }
#       initialDelaySeconds: 10
#       periodSeconds: 5
#       failureThreshold: 48        # up to ~4 min for first boot (table/container creation)
#     - type: Liveness
#       httpGet: { path: /alive, port: 8080 }
#       periodSeconds: 30
#     - type: Readiness
#       httpGet: { path: /health, port: 8080 }
#       periodSeconds: 10
az containerapp update --name ca-digitalbrain-kernel \
  --resource-group rg-digitalbrain-dev --yaml kernel-app.yaml

# 7. Point the SWA build at the kernel (used by deploy.yml — Section B)
gh variable set KERNEL_PUBLIC_URL --env production \
  --repo intochat/digitalbrain --body "https://$KERNEL_FQDN"
```

Notes:

- `Orleans__*` is the set Aspire injects locally and the image does NOT carry — without it the silo throws "Clustering has not been configured". `ServiceId` must stay stable forever (reminders and grain state are keyed by it).
- The journal store accepts only key-bearing connection strings (no managed-identity path in code) — hence account-key secrets for all four. Managed identity is a listed follow-up, not this stand.
- Orleans membership tables, journal/grain-state containers, and `kit-images` are created by the app on first boot; the account needs no pre-created tables/containers.
- First boot with the `v0.1.20` image runs but is UNGATED and CORS-less — the stand is considered live only after the Section B release is deployed. Don't publicize the FQDN before that.

## As built (2026-08-24)

Section A ran against different names than planned; these are authoritative:

| Planned | As built |
| --- | --- |
| `rg-digitalbrain-dev` | `intochat-rg` |
| `westeurope` | `polandcentral` |
| `stdigitalbraindev` | `stdbraindeve2c940` |
| — | SWA is `intochat-ui-webapp` (Free), serving purple-sky |

`cae-digitalbrain-dev` and `ca-digitalbrain-kernel` kept their names. The kernel
FQDN is `ca-digitalbrain-kernel.niceforest-c1f54c12.polandcentral.azurecontainerapps.io`.

Two corrections to Section A as written:

- `az containerapp secret set` alone changes nothing. Secrets are inert until an
  env var references them with `secretref:`; the app was created with only
  `storage-conn`, so the OpenAI, Qdrant, and auth env vars must be added in the
  same pass that creates their secrets.
- Health probes (step 6) were never applied. The rolled deployment relies on the
  release pipeline's `/health` poll instead.

The "auto-update the Container App image from deploy.yml" follow-up is now done,
and did NOT need an Entra app registration — the owner is a guest in the tenant
and cannot create one. A **user-assigned managed identity** (`id-digitalbrain-deploy`)
carries the GitHub federated credential instead: it is an ARM resource, so
subscription Owner is sufficient. Subject
`repo:intochat/digitalbrain:environment:production`, scoped Contributor on the
container app alone. Its client id and the target names live in the `production`
environment variables, which override the stale repo-level `AZURE_*` vars left
over from a different tenant.
Two things the v0.1.21 rollout taught us:

- The org enforces GitHub's immutable-ID OIDC subject claims, so the presented
  subject is `repo:intochat@290989167/digitalbrain@1275877089:environment:production`,
  not the documented plain-path form. The identity carries a federated credential
  for each (`github-production`, `github-production-ids`); the plain one never
  matches while that org setting stands.
- A rollout keeps the old revision serving the public FQDN until the new one is
  ready, so polling `/health` alone reports success against the code being
  replaced. The release job resolves the new revision by name and waits for its
  `runningState` before trusting `/health`.
## Section B — Code and pipeline changes (one release)

1. **Kernel Basic-auth middleware** (`src/Kernel/DigitalBrain.Kernel/Auth/`): active only when both `DigitalBrain__Auth__Username` and `__Password` are configured — unset means open, so local dev, Aspire, and the E2E fixture stay untouched. Constant-time comparison (`CryptographicOperations.FixedTimeEquals`). Returns 401 WITHOUT `WWW-Authenticate` (avoids the browser's native prompt). Exempt: `/health`, `/alive`, and OPTIONS preflights. Everything else — `/owner/commands`, SSE streams, `/kit/*`, voice, and the currently wide-open `/orleans` dashboard — is behind it.
2. **`GET /auth/check`** — inside the guard, returns 204. The login screen's probe.
3. **Kernel CORS**: `AddCors`/`UseCors` allowing exactly `DigitalBrain__Cors__AllowedOrigin` (when configured), any header/method, no credentials mode (`Authorization` rides preflight fine without it). Unset = no CORS = current behavior.
4. **Flutter login screen** (shell): gates client construction in `main.dart`. Username + password → probe `/auth/check` with `Authorization: Basic …` → on success the existing `CookieHttpClient.send()` choke point attaches the header to every request (covers SSE — they are plain `http.Request` sends). Creds held in memory + `sessionStorage` on web so refresh doesn't re-prompt; wrong creds show an inline error.
5. **deploy.yml**: add `--dart-define=DIGITALBRAIN_UI_BASE=${{ vars.KERNEL_PUBLIC_URL }}` to the `Web release build` step, so the deployed shell stops calling its own dead origin.
6. **Tests**: kernel middleware unit tests (disabled-when-unset, valid, invalid, exempt paths, preflight); Flutter widget test for the login gate; existing suites unaffected (auth and CORS both off when env unset).

## Rollout order

1. Run Section A (kernel boots on `v0.1.20`, ungated — do not share the URL yet).
2. Land Section B, cut release `v0.1.21` → deploy.yml publishes the image and redeploys the SWA with the dart-define.
3. `az containerapp update --name ca-digitalbrain-kernel --resource-group rg-digitalbrain-dev --image docker.io/vhorbachov/digitalbrain-kernel:v0.1.21`
4. Smoke: open purple-sky → login screen → wrong password rejected → right password → send a chat message → reply streams; `curl https://$KERNEL_FQDN/owner/commands -X POST` without the header → 401; journal blobs appear in `stdigitalbraindev`.

## Cost

~€12–15/mo for the always-on 0.5 vCPU / 1 GiB consumption replica; storage pennies; SWA free tier; Qdrant Cloud free tier; OpenAI usage-based (nano).

## Follow-ups (explicitly out of scope)

- Auto-update the Container App image from deploy.yml on release (needs an Azure federated credential for the workflow).
- Real auth (multi-user principals replacing the fixed `HttpActor` GUID) — the middleware and login screen are its placeholders.
- Managed identity for storage (requires a `ServiceUri` overload in `AzureOrleansJournalHosting`).
- Multi-replica Orleans (silo/gateway ports + ACA internal TCP).
- Verify at rollout: Qdrant connection parser accepts the cloud endpoint format; if the `Key=` segment is rejected for any reason, fix the parser rather than dropping auth.
