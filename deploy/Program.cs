using System.Collections.Generic;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Storage = Pulumi.AzureNative.Storage;
using StorageInputs = Pulumi.AzureNative.Storage.Inputs;
using Authorization = Pulumi.AzureNative.Authorization;
using Cognitive = Pulumi.AzureNative.CognitiveServices;
using CognitiveInputs = Pulumi.AzureNative.CognitiveServices.Inputs;
using OpInsights = Pulumi.AzureNative.OperationalInsights;
using OpInsightsInputs = Pulumi.AzureNative.OperationalInsights.Inputs;
using AppInsights = Pulumi.AzureNative.ApplicationInsights;
using App = Pulumi.AzureNative.App;
using AppInputs = Pulumi.AzureNative.App.Inputs;
using Web = Pulumi.AzureNative.Web;
using WebInputs = Pulumi.AzureNative.Web.Inputs;

namespace DigitalBrain.Deploy;

// Minimal Pulumi program for DigitalBrain / NeuroOS. Provisions only what the runtime actually uses:
// a resource group, one StorageV2 account (Orleans Table clustering + Blob grain/journal), Azure OpenAI
// (gpt-4o-mini "chat"), Log Analytics + App Insights, an ACA managed environment, a kernel container app
// with an external Auto-transport ingress, and a Telegram transport container app (external /webhook ingress).
// Replaces the vendored DeploymentKit.
internal static class Program
{
    private const string Region = "westeurope";
    private const string ResourceGroupName = "digitalbrain-rg";
    private const string EnvSuffix = "prod";
    private const string ChatDeploymentName = "chat";

    // Images live in private Docker Hub repos under the owner's personal account. ACA authenticates the pull
    // via AppInputs.RegistryCredentialsArgs (server=docker.io) with a Docker Hub PAT stored as a Container App
    // secret (DockerHubPasswordSecret below), since the repos are private.
    private const string DockerHubUsername = "vhorbachov";
    private const string DockerHubPasswordSecret = "dockerhub-password";
    private const string KernelImageRepository = "docker.io/vhorbachov/digitalbrain-kernel";
    private const string TelegramImageRepository = "docker.io/vhorbachov/digitalbrain-telegram";

    // Container App secret names backing the NeuroOS runtime contract.
    private const string StorageConnectionSecret = "digitalbrain-storage-connection";
    private const string OpenAiKeySecret = "digitalbrain-openai-key";
    private const string CheckpointKeySecret = "digitalbrain-checkpoint-key";

    // Telegram transport secrets — bot token + shared internal service key.
    private const string TelegramBotTokenSecret = "telegram-bot-token";
    private const string InternalServiceKeySecret = "digitalbrain-internal-service-key";

    // The environment + kernel app were previously created under DeploymentKit's "app-runtime" component. Alias to that old
    // parent URN so Pulumi re-parents them to the stack root in place instead of replacing the live resources.
    private const string LegacyRuntimeComponentUrn =
        "urn:pulumi:dev::digitalbrain-deploy::DeploymentKit:deploymentkit:DeploymentKitApp::digitalbrain-app-runtime-prod";

    private static Task<int> Main() => Pulumi.Deployment.RunAsync(Provision);

    private static IDictionary<string, object?> Provision()
    {
        var config = new Config();
        var imageTag = config.Get("imageTag")
            ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_IMAGE_TAG")
            ?? "latest";

        // CI injects the AES checkpoint-encryption key as a secret env var (from a GitHub Actions secret) so it
        // never lives in git; local runs can instead use `pulumi config set --secret checkpointKey ...`.
        var checkpointKeyEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_CHECKPOINT_KEY");
        var checkpointKey = config.GetSecret("checkpointKey")
            ?? (string.IsNullOrEmpty(checkpointKeyEnv) ? null : Output.CreateSecret(checkpointKeyEnv))
            ?? throw new System.InvalidOperationException(
                "Checkpoint key required: set env DIGITALBRAIN_CHECKPOINT_KEY (CI secret) " +
                "or `pulumi config set --secret digitalbrain-deploy:checkpointKey <base64-32-bytes>` (local).");

        // Telegram transport secrets. GetSecret returns null when not set so a token-less deploy boots idle
        // (transport skips webhook setup when BotToken is empty — same contract as Aspire dev wiring).
        // Set via: pulumi config set --secret telegramBotToken <value>
        //          pulumi config set --secret internalServiceKey <value>
        var telegramBotToken = config.GetSecret("telegramBotToken") ?? Output.CreateSecret(string.Empty);
        var internalServiceKey = config.GetSecret("internalServiceKey") ?? Output.CreateSecret(string.Empty);

        // Docker Hub PAT (read scope is enough — ACA only pulls, never pushes) backing the private
        // vhorbachov/digitalbrain-kernel and -telegram repos. Same CI-secret-or-local-config contract as
        // checkpointKey above.
        var dockerHubTokenEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_DOCKERHUB_TOKEN");
        var dockerHubToken = config.GetSecret("dockerHubToken")
            ?? (string.IsNullOrEmpty(dockerHubTokenEnv) ? null : Output.CreateSecret(dockerHubTokenEnv))
            ?? throw new System.InvalidOperationException(
                "Docker Hub token required: set env DIGITALBRAIN_DOCKERHUB_TOKEN (CI secret) " +
                "or `pulumi config set --secret digitalbrain-deploy:dockerHubToken <PAT>` (local).");

        var resourceGroup = new ResourceGroup(ResourceGroupName, new ResourceGroupArgs
        {
            ResourceGroupName = ResourceGroupName,
            Location = Region,
            Tags = StandardTags("resource-group")
        });

        var storage = new Storage.StorageAccount("digitalbrainstprod", new Storage.StorageAccountArgs
        {
            AccountName = "digitalbrainstprod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Kind = Storage.Kind.StorageV2,
            Sku = new StorageInputs.SkuArgs { Name = Storage.SkuName.Standard_LRS },
            AccessTier = Storage.AccessTier.Hot,
            AllowBlobPublicAccess = false,
            AllowSharedKeyAccess = true,
            EnableHttpsTrafficOnly = true,
            MinimumTlsVersion = Storage.MinimumTlsVersion.TLS1_2,
            NetworkRuleSet = new StorageInputs.NetworkRuleSetArgs
            {
                Bypass = Storage.Bypass.AzureServices,
                DefaultAction = Storage.DefaultAction.Allow
            },
            Tags = StandardTags("storage-account")
        });

        // Sync blob container (M11 checkpoint-based local<->cloud sync, Task 20): checkpoint backup/restore
        // blobs land here, scoped to the same storage account as clustering/grainstate/journal above — no new
        // storage account needed. PublicAccess.None matches the account-level AllowBlobPublicAccess = false.
        var syncContainer = new Storage.BlobContainer("digitalbrain-sync", new Storage.BlobContainerArgs
        {
            ContainerName = "sync",
            AccountName = storage.Name,
            ResourceGroupName = resourceGroup.Name,
            PublicAccess = Storage.PublicAccess.None
        });

        var storageKey = Storage.ListStorageAccountKeys.Invoke(new Storage.ListStorageAccountKeysInvokeArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AccountName = storage.Name
        }).Apply(keys => keys.Keys[0].Value);

        var storageConnectionString = Output.CreateSecret(Output.Tuple(storage.Name, storageKey).Apply(t =>
            $"DefaultEndpointsProtocol=https;AccountName={t.Item1};AccountKey={t.Item2};EndpointSuffix=core.windows.net"));

        var openAi = new Cognitive.Account("digitalbrainopenaiprod", new Cognitive.AccountArgs
        {
            AccountName = "digitalbrainopenaiprod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Kind = "OpenAI",
            Sku = new CognitiveInputs.SkuArgs { Name = "S0" },
            Identity = new CognitiveInputs.IdentityArgs { Type = Cognitive.ResourceIdentityType.SystemAssigned },
            Properties = new CognitiveInputs.AccountPropertiesArgs
            {
                CustomSubDomainName = "digitalbrainopenaiprod",
                PublicNetworkAccess = Cognitive.PublicNetworkAccess.Enabled
            },
            Tags = StandardTags("azure-openai")
        });

        var chatDeployment = new Cognitive.Deployment(ChatDeploymentName, new Cognitive.DeploymentArgs
        {
            DeploymentName = ChatDeploymentName,
            AccountName = openAi.Name,
            ResourceGroupName = resourceGroup.Name,
            Sku = new CognitiveInputs.SkuArgs { Name = "GlobalStandard", Capacity = 10 },
            Properties = new CognitiveInputs.DeploymentPropertiesArgs
            {
                Model = new CognitiveInputs.DeploymentModelArgs
                {
                    Format = "OpenAI",
                    Name = "gpt-4o-mini",
                    Version = "2024-07-18"
                }
            }
        });

        var openAiEndpoint = openAi.Properties.Apply(p => p.Endpoint ?? string.Empty);
        var openAiKey = Output.CreateSecret(Cognitive.ListAccountKeys.Invoke(new Cognitive.ListAccountKeysInvokeArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AccountName = openAi.Name
        }).Apply(keys => keys.Key1 ?? string.Empty));

        var workspace = new OpInsights.Workspace("digitalbrain-log-prod", new OpInsights.WorkspaceArgs
        {
            WorkspaceName = "digitalbrain-log-prod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Sku = new OpInsightsInputs.WorkspaceSkuArgs { Name = OpInsights.WorkspaceSkuNameEnum.PerGB2018 },
            RetentionInDays = 90,
            Tags = StandardTags("log-analytics")
        });

        var appInsights = new AppInsights.Component("digitalbrain-ai-prod", new AppInsights.ComponentArgs
        {
            ResourceName = "digitalbrain-ai-prod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Kind = "web",
            ApplicationType = AppInsights.ApplicationType.Web,
            WorkspaceResourceId = workspace.Id,
            Tags = StandardTags("application-insights")
        });
        var appInsightsConnectionString = appInsights.ConnectionString;

        var workspaceSharedKey = Output.CreateSecret(OpInsights.GetSharedKeys.Invoke(new OpInsights.GetSharedKeysInvokeArgs
        {
            ResourceGroupName = resourceGroup.Name,
            WorkspaceName = workspace.Name
        }).Apply(k => k.PrimarySharedKey ?? string.Empty));

        var containerEnvironment = new App.ManagedEnvironment("digitalbrain-cae-prod", new App.ManagedEnvironmentArgs
        {
            EnvironmentName = "digitalbrain-cae-prod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            AppLogsConfiguration = new AppInputs.AppLogsConfigurationArgs
            {
                Destination = "log-analytics",
                LogAnalyticsConfiguration = new AppInputs.LogAnalyticsConfigurationArgs
                {
                    CustomerId = workspace.CustomerId,
                    SharedKey = workspaceSharedKey
                }
            },
            Tags = StandardTags("container-apps-environment")
        }, AliasOldRuntimeParent());

        var kernelImage = Output.Format($"{KernelImageRepository}:{imageTag}");
        var telegramImage = Output.Format($"{TelegramImageRepository}:{imageTag}");

        var kernelApp = new App.ContainerApp("digitalbrain-jobs", new App.ContainerAppArgs
        {
            ContainerAppName = "digitalbrain-jobs",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            ManagedEnvironmentId = containerEnvironment.Id,
            // System-assigned identity backs the Storage Table/Blob Data Contributor role assignments below,
            // letting Orleans clustering/grain-storage/journal authenticate via managed identity instead of
            // the account key once DigitalBrain.Kernel's useManagedIdentity switch is live (Task 18, step 1/2).
            Identity = new AppInputs.ManagedServiceIdentityArgs { Type = App.ManagedServiceIdentityType.SystemAssigned },
            Configuration = new AppInputs.ConfigurationArgs
            {
                Ingress = new AppInputs.IngressArgs
                {
                    External = true,
                    TargetPort = 8080,
                    Transport = "Auto"
                },
                Secrets =
                {
                    new AppInputs.SecretArgs { Name = StorageConnectionSecret, Value = storageConnectionString },
                    new AppInputs.SecretArgs { Name = OpenAiKeySecret, Value = openAiKey },
                    new AppInputs.SecretArgs { Name = CheckpointKeySecret, Value = checkpointKey },
                    new AppInputs.SecretArgs { Name = DockerHubPasswordSecret, Value = dockerHubToken }
                },
                Registries =
                {
                    new AppInputs.RegistryCredentialsArgs
                    {
                        Server = "docker.io",
                        Username = DockerHubUsername,
                        PasswordSecretRef = DockerHubPasswordSecret
                    }
                }
            },
            Template = new AppInputs.TemplateArgs
            {
                Containers =
                {
                    new AppInputs.ContainerArgs
                    {
                        Name = "jobs",
                        Image = kernelImage,
                        Resources = new AppInputs.ContainerResourcesArgs { Cpu = 1.0, Memory = "2Gi" },
                        Probes =
                        {
                            new AppInputs.ContainerAppProbeArgs
                            {
                                Type = App.Type.Liveness,
                                HttpGet = new AppInputs.ContainerAppProbeHttpGetArgs { Path = "/alive", Port = 8080 },
                                InitialDelaySeconds = 10,
                                PeriodSeconds = 15
                            },
                            new AppInputs.ContainerAppProbeArgs
                            {
                                Type = App.Type.Readiness,
                                HttpGet = new AppInputs.ContainerAppProbeHttpGetArgs { Path = "/health", Port = 8080 },
                                InitialDelaySeconds = 10,
                                PeriodSeconds = 15
                            }
                        },
                        Env =
                        {
                            new AppInputs.EnvironmentVarArgs { Name = "ASPNETCORE_ENVIRONMENT", Value = "Production" },
                            new AppInputs.EnvironmentVarArgs { Name = "DIGITALBRAIN_WEB_PORT", Value = "8080" },
                            new AppInputs.EnvironmentVarArgs { Name = "DIGITALBRAIN_ENV", Value = "cloud" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__Provider", Value = "azureopenai" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__Model", Value = ChatDeploymentName },
                            new AppInputs.EnvironmentVarArgs { Name = "ConnectionStrings__clustering", SecretRef = StorageConnectionSecret },
                            new AppInputs.EnvironmentVarArgs { Name = "ConnectionStrings__grainstate", SecretRef = StorageConnectionSecret },
                            new AppInputs.EnvironmentVarArgs { Name = "ConnectionStrings__journal", SecretRef = StorageConnectionSecret },
                            // Task 21: CheckpointBackupTrigger's "sync" BlobContainerClient (KernelServices.AddCheckpointSync)
                            // falls back to this connection string whenever useManagedIdentity is false. It's set here
                            // unconditionally (same StorageConnectionSecret as clustering/grainstate/journal — one storage
                            // account) so it's never null even before useManagedIdentity's real-identity branch actually
                            // runs; once DigitalBrain__Storage__AccountName below flips useManagedIdentity on, this var is
                            // simply unused by that branch (harmless to leave set).
                            new AppInputs.EnvironmentVarArgs { Name = "ConnectionStrings__sync", SecretRef = StorageConnectionSecret },
                            // Read by DigitalBrain.Kernel/Program.cs to flip useManagedIdentity on. Never set in
                            // Aspire/local config, so local dev + existing tests keep using the connection
                            // strings above unchanged (shared-key access stays enabled until Task 18 step 5).
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Storage__AccountName", Value = storage.Name },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__AzureOpenAIEndpoint", Value = openAiEndpoint },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__AzureOpenAIKey", SecretRef = OpenAiKeySecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Checkpoint__Key", SecretRef = CheckpointKeySecret },
                            new AppInputs.EnvironmentVarArgs { Name = "APPLICATIONINSIGHTS_CONNECTION_STRING", Value = appInsightsConnectionString }
                        }
                    }
                },
                Scale = new AppInputs.ScaleArgs { MinReplicas = 2, MaxReplicas = 5 },
                // Give Orleans time to drain (deactivate grains, finish in-flight requests) on scale-in/redeploy before ACA SIGKILLs the pod.
                TerminationGracePeriodSeconds = 90
            },
            Tags = StandardTags("container-app-jobs")
        }, AliasOldRuntimeParent());

        // Grant the kernel's system-assigned identity data-plane access to the storage account (Task 18,
        // step 2) and to the OpenAI account (Task 19, step 1). GUIDs verified against Microsoft Learn's
        // built-in-roles/storage.md and dotnet/ai/azure-ai-services-authentication.md sources, not trusted
        // from memory. GrantKernelRole is generalized over the target scope (storage.Id vs openAi.Id) since
        // both are just "system-assigned identity, one RBAC role, one resource scope" — no need for a second
        // near-identical helper.
        var kernelPrincipalId = kernelApp.Identity.Apply(identity => identity!.PrincipalId!);
        GrantKernelRole("kernel-storage-table-contributor", "0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3", storage.Id); // Storage Table Data Contributor
        GrantKernelRole("kernel-storage-blob-contributor", "ba92f5b4-2d11-453d-a403-e96b0029c9fe", storage.Id); // Storage Blob Data Contributor
        // Kernel identity isn't granted access until this deploys; the key-based path (openAiKey/OpenAiKeySecret
        // above) stays wired unchanged so DigitalBrainChat.cs's key branch keeps working until a verified,
        // separate follow-up deploy removes the key and flips DisableLocalAuth (Task 19 steps 2/4, out of scope here).
        GrantKernelRole("kernel-openai-user", "5e0bd9bd-7b93-4f28-af87-19fc36ad61bd", openAi.Id); // Cognitive Services OpenAI User

        void GrantKernelRole(string resourceName, string roleDefinitionGuid, Input<string> scope) =>
            _ = new Authorization.RoleAssignment(resourceName, new Authorization.RoleAssignmentArgs
            {
                PrincipalId = kernelPrincipalId,
                PrincipalType = Authorization.PrincipalType.ServicePrincipal,
                RoleDefinitionId = $"/providers/Microsoft.Authorization/roleDefinitions/{roleDefinitionGuid}",
                Scope = scope
            });

        // The Telegram transport calls the kernel's gRPC gateway. The kernel app's external FQDN is reachable from
        // within the same ACA environment, so we build the address from LatestRevisionFqdn. Must be HTTPS
        // because ACA external ingress always terminates TLS. Same key the Aspire dev wiring sets:
        // "DigitalBrain:GatewayAddress" (colon separator, mirrored in transport Program.cs).
        var kernelGatewayAddress = kernelApp.LatestRevisionFqdn.Apply(fqdn => $"https://{fqdn}");

        var telegramTransport = new App.ContainerApp("digitalbrain-telegram", new App.ContainerAppArgs
        {
            ContainerAppName = "digitalbrain-telegram",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            ManagedEnvironmentId = containerEnvironment.Id,
            Configuration = new AppInputs.ConfigurationArgs
            {
                // External ingress so Telegram's servers can POST to /webhook.
                Ingress = new AppInputs.IngressArgs
                {
                    External = true,
                    TargetPort = 8080,
                    Transport = "Http"
                },
                Secrets =
                {
                    new AppInputs.SecretArgs { Name = TelegramBotTokenSecret, Value = telegramBotToken },
                    new AppInputs.SecretArgs { Name = InternalServiceKeySecret, Value = internalServiceKey },
                    new AppInputs.SecretArgs { Name = DockerHubPasswordSecret, Value = dockerHubToken }
                },
                Registries =
                {
                    new AppInputs.RegistryCredentialsArgs
                    {
                        Server = "docker.io",
                        Username = DockerHubUsername,
                        PasswordSecretRef = DockerHubPasswordSecret
                    }
                }
            },
            Template = new AppInputs.TemplateArgs
            {
                Containers =
                {
                    new AppInputs.ContainerArgs
                    {
                        Name = "telegram",
                        Image = telegramImage,
                        Resources = new AppInputs.ContainerResourcesArgs { Cpu = 0.25, Memory = "0.5Gi" },
                        Probes =
                        {
                            new AppInputs.ContainerAppProbeArgs
                            {
                                Type = App.Type.Liveness,
                                HttpGet = new AppInputs.ContainerAppProbeHttpGetArgs { Path = "/alive", Port = 8080 },
                                InitialDelaySeconds = 10,
                                PeriodSeconds = 15
                            },
                            new AppInputs.ContainerAppProbeArgs
                            {
                                Type = App.Type.Readiness,
                                HttpGet = new AppInputs.ContainerAppProbeHttpGetArgs { Path = "/health", Port = 8080 },
                                InitialDelaySeconds = 10,
                                PeriodSeconds = 15
                            }
                        },
                        Env =
                        {
                            new AppInputs.EnvironmentVarArgs { Name = "ASPNETCORE_ENVIRONMENT", Value = "Production" },
                            new AppInputs.EnvironmentVarArgs { Name = "ASPNETCORE_HTTP_PORTS", Value = "8080" },
                            // Same keys read by transport Program.cs and set by WireTelegramTransport for dev parity.
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__GatewayAddress", Value = kernelGatewayAddress },
                            new AppInputs.EnvironmentVarArgs { Name = "Telegram__BotToken", SecretRef = TelegramBotTokenSecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__InternalServiceKey", SecretRef = InternalServiceKeySecret },
                            new AppInputs.EnvironmentVarArgs { Name = "Telegram__PackName", Value = "DigitalBrain.Telegram.Responder" },
                            new AppInputs.EnvironmentVarArgs { Name = "Telegram__ConfigScope", Value = "default" },
                            new AppInputs.EnvironmentVarArgs { Name = "APPLICATIONINSIGHTS_CONNECTION_STRING", Value = appInsightsConnectionString }
                        }
                    }
                },
                Scale = new AppInputs.ScaleArgs { MinReplicas = 1, MaxReplicas = 3 }
            },
            Tags = StandardTags("container-app-telegram")
        });

        // Flutter web bundle host. "Bring your own build" mode: no RepositoryUrl/Branch/RepositoryToken —
        // this repo's own deploy-flutter-web.yml workflow builds app/build/web and uploads it directly via
        // Azure/static-web-apps-deploy@v1, so the Static Web App is never linked to a GitHub repo in Azure.
        // Those repository-integration fields are optional on StaticSiteArgs; omitting them compiles fine and
        // just leaves the resource without Azure-managed CI (which we don't want — CI already lives in Actions).
        var flutterWebSite = new Web.StaticSite("digitalbrain-web-prod", new Web.StaticSiteArgs
        {
            Name = "digitalbrain-web-prod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Sku = new WebInputs.SkuDescriptionArgs { Name = "Free", Tier = "Free" },
            Tags = StandardTags("static-web-app")
        });

        // CI reads this stack output (see swaDeploymentToken below) into the SWA_DEPLOYMENT_TOKEN repo secret
        // that Azure/static-web-apps-deploy@v1 authenticates uploads with.
        var swaSecrets = Web.ListStaticSiteSecrets.Invoke(new Web.ListStaticSiteSecretsInvokeArgs
        {
            Name = flutterWebSite.Name,
            ResourceGroupName = resourceGroup.Name
        });
        var swaDeploymentToken = Output.CreateSecret(swaSecrets.Apply(s => s.Properties["apiKey"]));

        return new Dictionary<string, object?>
        {
            ["resourceGroup"] = resourceGroup.Name,
            ["storageAccount"] = storage.Name,
            ["openAiEndpoint"] = openAiEndpoint,
            ["chatDeployment"] = ChatDeploymentName,
            ["kernelApp"] = kernelApp.Name,
            ["kernelFqdn"] = kernelApp.LatestRevisionFqdn,
            ["telegramApp"] = telegramTransport.Name,
            ["telegramFqdn"] = telegramTransport.LatestRevisionFqdn,
            ["imageTag"] = imageTag,
            ["environment"] = EnvSuffix,
            ["swaDeploymentToken"] = swaDeploymentToken,
            ["swaDefaultHostname"] = flutterWebSite.DefaultHostname
        };
    }

    private static CustomResourceOptions AliasOldRuntimeParent() =>
        new() { Aliases = { new Alias { ParentUrn = LegacyRuntimeComponentUrn } } };

    private static InputMap<string> StandardTags(string resourceType) => new Dictionary<string, string>
    {
        ["Environment"] = EnvSuffix,
        ["Project"] = "Application",
        ["Owner"] = "Application-DevOps",
        ["CorrelationId"] = "unknown",
        ["CreatedBy"] = "Pulumi",
        ["ManagedBy"] = "Pulumi",
        ["ResourceType"] = resourceType
    };
}
