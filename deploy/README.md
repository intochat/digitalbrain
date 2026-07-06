# DigitalBrain.Deploy

Minimal Pulumi C# program (Pulumi.AzureNative, ~250 LOC) that provisions exactly the Azure resources
the NeuroOS kernel needs: RG, StorageV2 (for Orleans clustering/grain/journal), Azure OpenAI (gpt-4o-mini),
Log Analytics + App Insights, ACA Managed Environment, and the kernel ContainerApp (`digitalbrain-jobs`)
with public Auto ingress on 8080 (gRPC-Web + gRPC).

The previous vendored DeploymentKit (hundreds of files) was deleted; this single `Program.cs` is the
current "deployment kit". See DEPLOY-STATUS.md for live resources and history.

**Preferred deployment:** push to `master` (triggers `.github/workflows/deploy.yml` which builds, tests,
pushes the kernel + Telegram transport images to **private** Docker Hub repos
(`docker.io/vhorbachov/digitalbrain-kernel`, `-telegram`), authenticated via `vars.DOCKERHUB_USERNAME` +
`secrets.DOCKERHUB_TOKEN`, then `pulumi up --stack dev` using Azure OIDC + azblob state). Since the repos
are private, ACA authenticates the pull with the same Docker Hub PAT via `RegistryCredentialsArgs`
(`deploy/Program.cs`).

- Stack: `dev`
- Image tag driven by `imageTag` config or DIGITALBRAIN_IMAGE_TAG (workflow uses git sha or input)
- Checkpoint encryption key (required): from `secrets.CHECKPOINT_KEY` as DIGITALBRAIN_CHECKPOINT_KEY or local `pulumi config set --secret checkpointKey`
- Docker Hub PAT (required, read scope is enough): from `secrets.DOCKERHUB_TOKEN` as DIGITALBRAIN_DOCKERHUB_TOKEN or local `pulumi config set --secret dockerHubToken`

Local (for verification only; prod deploys are GH Actions only):

```pwsh
$env:PULUMI_HOME = 'E:/tools/pulumi-home'
$env:PULUMI_CONFIG_PASSPHRASE = 'digitalbrain-dryrun'
$env:AZURE_STORAGE_ACCOUNT = 'digitalbrainstprod'
# ... full env + pulumi login azblob://pulumi-state ...
dotnet run --project deploy/DigitalBrain.Deploy.csproj -- up --stack dev -- --image-tag sp1
```

The project is included in `Brain.slnx` under `/deploy/`.
