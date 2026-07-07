# DigitalBrain.Deploy

Minimal Pulumi C# program (Pulumi.AzureNative, ~250 LOC) that provisions exactly the Azure resources
the NeuroOS kernel needs: RG, StorageV2 (for Orleans clustering/grain/journal), Azure OpenAI (gpt-4o-mini),
Log Analytics + App Insights, ACA Managed Environment, and the kernel ContainerApp (`digitalbrain-jobs`)
with public Auto ingress on 8080 (gRPC-Web + gRPC).

The previous vendored DeploymentKit (hundreds of files) was deleted; this single `Program.cs` is the
current "deployment kit". See DEPLOY-STATUS.md for live resources and history.

**Preferred deployment:** publish a GitHub Release. `.github/workflows/deploy.yml` builds/tests, pushes
the kernel + Telegram transport images to **private** Docker Hub repos
(`docker.io/vhorbachov/digitalbrain-kernel`, `-telegram`), runs `pulumi up --stack dev` using Azure OIDC +
azblob state, ensures `digitalbrain-web-prod` exists in Azure Static Web Apps with `az staticwebapp`, uploads
the Flutter web bundle, and smoke-tests the kernel and SWA endpoints. Since the repos are private, ACA
authenticates the pull with the same Docker Hub PAT via `RegistryCredentialsArgs` (`deploy/Program.cs`).

- Stack: `dev`
- Image tag is `DIGITALBRAIN_IMAGE_TAG` in CI (release tag), falling back to Pulumi `imageTag` config locally
- Checkpoint encryption key (required): from `secrets.CHECKPOINT_KEY` as DIGITALBRAIN_CHECKPOINT_KEY or local `pulumi config set --secret checkpointKey`
- Docker Hub PAT (required, read/write for CI push and ACA pull): from `secrets.DOCKERHUB_TOKEN` as DIGITALBRAIN_DOCKERHUB_TOKEN or local `pulumi config set --secret dockerHubToken`
- Static Web App: `digitalbrain-web-prod`, created/read by the release workflow through Azure CLI, then deployed with `Azure/static-web-apps-deploy@v1`
- Production domains: `www.digitalbrain.tech` for Flutter web and `api.digitalbrain.tech` for the kernel Container App. The release workflow keeps these custom hostnames registered and builds Flutter with `KERNEL_ENDPOINT=https://api.digitalbrain.tech`.
- Optional GitHub Actions vars can override the defaults: `DIGITALBRAIN_WEB_HOSTNAME`, `DIGITALBRAIN_WEB_APEX_HOSTNAME`, and `DIGITALBRAIN_KERNEL_HOSTNAME`. The workflow passes those through to Pulumi so backend CORS stays aligned. For local Pulumi verification, the equivalent config keys are `webHostname`, `webApexHostname`, `kernelHostname`, and `staticWebAppsHostname`.

Local (for verification only; prod deploys are GH Actions only):

```pwsh
$env:PULUMI_HOME = 'E:/tools/pulumi-home'
$env:PULUMI_CONFIG_PASSPHRASE = 'digitalbrain-dryrun'
$env:AZURE_STORAGE_ACCOUNT = 'digitalbrainstprod'
# ... full env + pulumi login azblob://pulumi-state ...
dotnet run --project deploy/DigitalBrain.Deploy.csproj -- up --stack dev -- --image-tag sp1
```

The project is included in `Brain.slnx` under `/deploy/`.
