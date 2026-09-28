# IntoChat deployment

A Pulumi program that deploys IntoChat to Azure from its AppHost manifest. [DeploymentKit](https://github.com/anton-kharchenko/DeploymentKit) lays the foundation; every composed module deploys what it owns; every secret lands in Key Vault.

```
AppHost ──(--publisher manifest)──► aspire-manifest.json ──► pulumi up (this program)
                                                               ├─ DeploymentKit: resource group, VNet, Key Vault, Insights, ACR, storage
                                                               ├─ runtime identity + workload-profiles Container Apps environment
                                                               ├─ module deployments ([ModuleDeployment] on each module)
                                                               └─ brain runtime app: manifest env, secrets as Key Vault references
```

## How modules take part

- **Secrets** need nothing module-specific. Every secret parameter a module declares in its Aspire hosting (`AddParameter(..., secret: true)`) is stack config of the same name. Any runtime environment variable that reads one, directly or through a connection string, becomes a Key Vault secret the runtime references through its managed identity.
- **Resources** a module runs (containers, databases) are deployed by the module's `Deployment` project: an `IDigitalBrainModuleDeployment` named by `[ModuleDeployment("…")]` on the module. It may add DeploymentKit services in `ConfigureFoundation`, deploys in `Deploy`, and `Provide`s every value the runtime's manifest environment reads from its resources.
- A value no module provides fails the deployment up front, naming the missing `{resource.path}`.

| Module | Deployment | Deploys |
| --- | --- | --- |
| Qdrant | `Qdrant/DigitalBrain.Modules.Qdrant.Deployment` | Qdrant (manifest image) as an internal app, data on Azure Files |
| ClickHouse | `ClickHouse/DigitalBrain.Modules.ClickHouse.Deployment` | ClickHouse (manifest image) as an internal app, data on Azure Files |
| Compute | `DigitalBrain/Compute/DigitalBrain.Modules.Compute.Deployment` | The ledger on DeploymentKit's PostgreSQL Flexible Server |
| AI | `AI/DigitalBrain.Modules.AI.Deployment` | Ollama, pulling every model the manifest names; provider keys are secrets |
| CSharp | `Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Deployment` | Session pool running the Sandbox image in its own environment and subnet |
| Gmail, Salesforce, Supabase, GitHub | — | Secrets only |

## Deploy

Prerequisites: Pulumi CLI, a Pulumi backend (`pulumi login`), Azure CLI logged in to the target subscription (or OIDC in CI), Docker for the sandbox image.

```powershell
# 1. The manifest of the profile being deployed.
dotnet run --project src/Applications/IntoChat/AppHost -- --publisher manifest `
    --output-path src/Applications/IntoChat/Deployment/aspire-manifest.json IntoChat:Profile=hosted

cd src/Applications/IntoChat/Deployment
pulumi stack select production        # or: pulumi stack init production

# 2. Settings.
pulumi config set digitalbrain:subscriptionId <subscription-id>
pulumi config set digitalbrain:runtimeImage docker.io/<user>/digitalbrain-kernel:<tag>

# 3. One secret per secret parameter in the manifest, plus plain parameters.
$manifest = Get-Content aspire-manifest.json | ConvertFrom-Json
$manifest.resources.PSObject.Properties | Where-Object { $_.Value.type -eq 'parameter.v0' } |
    ForEach-Object { "{0} secret={1}" -f $_.Name, [bool]$_.Value.inputs.value.secret }
pulumi config set --secret digitalbrain:openai-api-key <value>     # …and every other secret listed
pulumi config set digitalbrain:gmail-client-id <value>             # plain parameters without --secret

# 4. Deploy.
pulumi up
```

### With the CSharp module

The session pool needs its image in the registry before it can be created, and the Session Executor role id:

```powershell
pulumi config set digitalbrain:sessionExecutorRoleId (az role definition list --name "Azure ContainerApps Session Executor" --query "[0].name" -o tsv)
# After the first `pulumi up` created the registry (output registryLoginServer):
docker build -f src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Sandbox/Dockerfile.production -t <registry>/csharp-sandbox:<tag> .
az acr login --name <registry> ; docker push <registry>/csharp-sandbox:<tag>
pulumi config set digitalbrain:sandboxImage <registry>/csharp-sandbox:<tag>
pulumi up
```

`Dockerfile.production` bakes the brain client and every module's contracts into `/brain`, so scripts' `#:project /brain/...` lines resolve without a repository mount.

## Tests

`src/Applications/IntoChat/Tests/Deployment` runs this program under Pulumi mocks, without Azure: the hosted manifest in `Fixtures/` must deploy with every module discovered, nothing unresolved and every secret vaulted, and the sandbox deployment must isolate its pool. Regenerate the fixture with the command in step 1 when the AppHost changes.

## Known gaps

- DeploymentKit `1.0.0-preview.3` does not register its green-blue slot and traffic services, so `InfrastructureDeployer.DeployAsync` always fails its own check; `DeploymentKitFoundation` builds the provider itself and registers both. Drop the workaround once the kit is fixed upstream.
- DeploymentKit creates a consumption-only Container Apps environment; session pools and extra ports need workload profiles, so the brain runs in its own environment on a delegated subnet of the kit's VNet.
- Sandbox egress is limited to HTTPS by NSG; limiting it to the script edge alone needs Azure Firewall FQDN rules.
- ClickHouse seed scripts the AppHost copies into its local container are not part of the manifest.
- ClickHouse and Qdrant keep data on an Azure Files (SMB) share. ClickHouse merges need hard links and Qdrant does not support network filesystems, so move both to Azure Files NFS (Premium FileStorage, VNet-mounted) or a managed service before relying on them under load.
- Subnets default to `10.0.8.0/23` (apps) and `10.0.10.0/23` (sandbox) inside DeploymentKit's `10.0.0.0/16`; override with `digitalbrain:appsSubnet` / `digitalbrain:sandboxSubnet`.
- First deployments wait on the runtime's role assignments, but Azure RBAC can take minutes to propagate; if the first revision cannot read Key Vault or pull its image, run `pulumi up` again.
- Flutter web still deploys through the release workflow's Static Web Apps step.
- Existing production (`intochat-rg`) was created by hand; this program provisions a new resource group to cut over to.
