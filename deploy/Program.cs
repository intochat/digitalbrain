using System.Collections.Generic;
using Pulumi;
using Pulumi.AzureNative.Resources;
using App = Pulumi.AzureNative.App;
using AppInputs = Pulumi.AzureNative.App.Inputs;
using AppInsights = Pulumi.AzureNative.ApplicationInsights;
using Authorization = Pulumi.AzureNative.Authorization;
using Cognitive = Pulumi.AzureNative.CognitiveServices;
using CognitiveInputs = Pulumi.AzureNative.CognitiveServices.Inputs;
using OpInsights = Pulumi.AzureNative.OperationalInsights;
using OpInsightsInputs = Pulumi.AzureNative.OperationalInsights.Inputs;
using Storage = Pulumi.AzureNative.Storage;
using StorageInputs = Pulumi.AzureNative.Storage.Inputs;

namespace DigitalBrain.Deploy;

// Minimal Pulumi program for DigitalBrain / NeuroOS. Provisions only what the runtime actually uses:
// a resource group, one Storage account (Orleans Table clustering + Blob grain/journal), Azure OpenAI,
// Log Analytics + App Insights, an ACA managed environment, the kernel, and the public one-replica MCP/UI edge.
// Replaces the vendored DeploymentKit.
internal static class Program
{
    private const string Region = "westeurope";
    private const string ResourceGroupName = "digitalbrain-rg";
    private const string EnvSuffix = "prod";
    private const string ChatDeploymentName = "chat";
    private const string DefaultFrontendApexHostname = "digitalbrain.tech";
    private const string DefaultFrontendWwwHostname = "www.digitalbrain.tech";
    private const string DefaultFrontendStaticWebAppsHostname = "gentle-sand-0f4081803.7.azurestaticapps.net";
    private const string DefaultKernelCustomHostname = "api.digitalbrain.tech";
    private const string DefaultMcpCustomHostname = "mcp.digitalbrain.tech";
    private const string RequiredMcpAudience = "digitalbrain-v2";
    private const string RequiredUiAudience = "digitalbrain-v2-ui";

    // The image lives in a private Docker Hub repository. ACA authenticates the pull
    // via AppInputs.RegistryCredentialsArgs (server=docker.io) with a Docker Hub PAT stored as a Container App
    // secret (DockerHubPasswordSecret below), since the repository is private.
    private const string DockerHubPasswordSecret = "dockerhub-password";

    // Container App secret names backing the NeuroOS runtime contract.
    private const string OpenAiKeySecret = "digitalbrain-openai-key";
    private const string CheckpointKeySecret = "digitalbrain-checkpoint-key";
    private const string SessionSigningKeySecret = "digitalbrain-session-signing-key";
    private const string RuntimeStateKekSecret = "digitalbrain-runtime-state-kek-v1";
    private const string RuntimeStateSigningKeySecret = "digitalbrain-runtime-state-signing-key";
    private const string GoogleClientSecret = "digitalbrain-google-client-secret";
    private const string SalesforceClientSecret = "digitalbrain-salesforce-client-secret";

    // The environment + kernel app were previously created under DeploymentKit's "app-runtime" component. Alias to that old
    // parent URN so Pulumi re-parents them to the stack root in place instead of replacing the live resources.
    private const string LegacyRuntimeComponentUrn =
        "urn:pulumi:dev::digitalbrain-deploy::DeploymentKit:deploymentkit:DeploymentKitApp::digitalbrain-app-runtime-prod";

    private static Task<int> Main() => Pulumi.Deployment.RunAsync(Provision);

    private static IDictionary<string, object?> Provision()
    {
        var config = new Config();
        var dockerHubUsername = RequiredSetting(config, "dockerHubUsername", "DIGITALBRAIN_DOCKERHUB_USERNAME");
        var imageTag = Environment.GetEnvironmentVariable("DIGITALBRAIN_IMAGE_TAG")
            ?? config.Get("imageTag")
            ?? "latest";
        var frontendApexOrigin = ConfiguredHttpsOrigin(
            config,
            "DIGITALBRAIN_WEB_APEX_HOSTNAME",
            "webApexHostname",
            DefaultFrontendApexHostname);
        var frontendWwwOrigin = ConfiguredHttpsOrigin(
            config,
            "DIGITALBRAIN_WEB_HOSTNAME",
            "webHostname",
            DefaultFrontendWwwHostname);
        var frontendStaticWebAppsOrigin = ConfiguredHttpsOrigin(
            config,
            "DIGITALBRAIN_STATIC_WEB_APPS_HOSTNAME",
            "staticWebAppsHostname",
            DefaultFrontendStaticWebAppsHostname);
        var kernelCustomEndpoint = ConfiguredHttpsOrigin(
            config,
            "DIGITALBRAIN_KERNEL_HOSTNAME",
            "kernelHostname",
            DefaultKernelCustomHostname);
        var mcpCustomEndpoint = ConfiguredHttpsOrigin(
            config,
            "DIGITALBRAIN_MCP_HOSTNAME",
            "mcpHostname",
            DefaultMcpCustomHostname);

        var sessionSigningKey = RequiredSecret(config, "sessionSigningKey", "DIGITALBRAIN_SESSION_SIGNING_KEY");
        var runtimeStateKek = RequiredSecret(config, "runtimeStateKek", "DIGITALBRAIN_RUNTIME_STATE_KEK");
        var runtimeStateSigningKey = RequiredSecret(config, "runtimeStateSigningKey", "DIGITALBRAIN_RUNTIME_STATE_SIGNING_KEY");
        var googleClientId = RequiredSetting(config, "googleClientId", "DIGITALBRAIN_GOOGLE_CLIENT_ID");
        var googleClientSecret = RequiredSecret(config, "googleClientSecret", "DIGITALBRAIN_GOOGLE_CLIENT_SECRET");
        var googleRedirectUri = RequiredHttpsUri(config, "googleRedirectUri", "DIGITALBRAIN_GOOGLE_REDIRECT_URI");
        var salesforceClientId = RequiredSetting(config, "salesforceClientId", "DIGITALBRAIN_SALESFORCE_CLIENT_ID");
        var salesforceClientSecret = RequiredSecret(config, "salesforceClientSecret", "DIGITALBRAIN_SALESFORCE_CLIENT_SECRET");
        var salesforceRedirectUri = RequiredHttpsUri(config, "salesforceRedirectUri", "DIGITALBRAIN_SALESFORCE_REDIRECT_URI");
        var oidcIssuer = RequiredHttpsUri(config, "oidcIssuer", "DIGITALBRAIN_OIDC_ISSUER").TrimEnd('/');
        var oidcAudience = RequiredSetting(config, "oidcAudience", "DIGITALBRAIN_OIDC_AUDIENCE");
        var mcpAudience = RequiredSetting(config, "mcpAudience", "DIGITALBRAIN_MCP_AUDIENCE");
        var uiAudience = RequiredSetting(config, "uiAudience", "DIGITALBRAIN_UI_AUDIENCE");
        DemandExactSetting(mcpAudience, RequiredMcpAudience, "DIGITALBRAIN_MCP_AUDIENCE");
        DemandExactSetting(uiAudience, RequiredUiAudience, "DIGITALBRAIN_UI_AUDIENCE");
        DemandOAuthCallback(googleRedirectUri, kernelCustomEndpoint, "google", "DIGITALBRAIN_GOOGLE_REDIRECT_URI");
        DemandOAuthCallback(salesforceRedirectUri, kernelCustomEndpoint, "salesforce", "DIGITALBRAIN_SALESFORCE_REDIRECT_URI");
        var frontendOrigins = string.Join(',', frontendApexOrigin, frontendWwwOrigin, frontendStaticWebAppsOrigin);

        var checkpointKey = RequiredSecret(config, "checkpointKey", "DIGITALBRAIN_CHECKPOINT_KEY");
        var dockerHubToken = RequiredSecret(config, "dockerHubToken", "DIGITALBRAIN_DOCKERHUB_TOKEN");

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
            AllowSharedKeyAccess = false,
            EnableHttpsTrafficOnly = true,
            MinimumTlsVersion = Storage.MinimumTlsVersion.TLS1_2,
            NetworkRuleSet = new StorageInputs.NetworkRuleSetArgs
            {
                Bypass = Storage.Bypass.AzureServices,
                DefaultAction = Storage.DefaultAction.Allow
            },
            Tags = StandardTags("storage-account")
        });

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

        var kernelImage = Output.Format($"docker.io/{dockerHubUsername}/digitalbrain-kernel:{imageTag}");

        var kernelApp = new App.ContainerApp("digitalbrain-jobs", new App.ContainerAppArgs
        {
            ContainerAppName = "digitalbrain-jobs",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            ManagedEnvironmentId = containerEnvironment.Id,
            // System-assigned identity backs all Storage data-plane access. Shared-key access is disabled on
            // the account, so a missing role assignment fails closed instead of falling back to an account key.
            Identity = new AppInputs.ManagedServiceIdentityArgs { Type = App.ManagedServiceIdentityType.SystemAssigned },
            Configuration = new AppInputs.ConfigurationArgs
            {
                Ingress = new AppInputs.IngressArgs
                {
                    AllowInsecure = false,
                    External = true,
                    TargetPort = 8080,
                    Transport = "Auto"
                },
                Secrets =
                {
                    new AppInputs.SecretArgs { Name = OpenAiKeySecret, Value = openAiKey },
                    new AppInputs.SecretArgs { Name = CheckpointKeySecret, Value = checkpointKey },
                    new AppInputs.SecretArgs { Name = RuntimeStateKekSecret, Value = runtimeStateKek },
                    new AppInputs.SecretArgs { Name = RuntimeStateSigningKeySecret, Value = runtimeStateSigningKey },
                    new AppInputs.SecretArgs { Name = GoogleClientSecret, Value = googleClientSecret },
                    new AppInputs.SecretArgs { Name = SalesforceClientSecret, Value = salesforceClientSecret },
                    new AppInputs.SecretArgs { Name = DockerHubPasswordSecret, Value = dockerHubToken }
                },
                Registries =
                {
                    new AppInputs.RegistryCredentialsArgs
                    {
                        Server = "docker.io",
                        Username = dockerHubUsername,
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
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Profile", Value = "Production" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__ForwardedHeaders__TrustAzureContainerAppsIngress", Value = "true" },
                            new AppInputs.EnvironmentVarArgs { Name = "DIGITALBRAIN_WEB_PORT", Value = "8080" },
                            new AppInputs.EnvironmentVarArgs { Name = "DIGITALBRAIN_ENV", Value = "cloud" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__Provider", Value = "azureopenai" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__Model", Value = ChatDeploymentName },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Storage__AccountName", Value = storage.Name },
                            new AppInputs.EnvironmentVarArgs { Name = "Orleans__ClusterId", Value = "digitalbrain" },
                            new AppInputs.EnvironmentVarArgs { Name = "Orleans__ServiceId", Value = "digitalbrain" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__AzureOpenAIEndpoint", Value = openAiEndpoint },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__AzureOpenAIKey", SecretRef = OpenAiKeySecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Checkpoint__Key", SecretRef = CheckpointKeySecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__State__ActiveKekVersion", Value = "1" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__State__Keks__1", SecretRef = RuntimeStateKekSecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__State__SigningKey", SecretRef = RuntimeStateSigningKeySecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Google__ClientId", Value = googleClientId },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Google__ClientSecret", SecretRef = GoogleClientSecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Google__RedirectUri", Value = googleRedirectUri },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Salesforce__ClientId", Value = salesforceClientId },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Salesforce__ClientSecret", SecretRef = SalesforceClientSecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Salesforce__RedirectUri", Value = salesforceRedirectUri },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Salesforce__LoginUrl", Value = "https://login.salesforce.com" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Salesforce__ApiVersion", Value = "v61.0" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Cors__AllowedOrigins__0", Value = frontendApexOrigin },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Cors__AllowedOrigins__1", Value = frontendWwwOrigin },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Cors__AllowedOrigins__2", Value = frontendStaticWebAppsOrigin },
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

        var mcpImage = Output.Format($"docker.io/{dockerHubUsername}/digitalbrain-mcp:{imageTag}");
        var mcpApp = new App.ContainerApp("digitalbrain-mcp", new App.ContainerAppArgs
        {
            ContainerAppName = "digitalbrain-mcp",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            ManagedEnvironmentId = containerEnvironment.Id,
            Identity = new AppInputs.ManagedServiceIdentityArgs { Type = App.ManagedServiceIdentityType.SystemAssigned },
            // MCP is the sole edge for Flutter runtime (authenticated gRPC UI transport only). Kernel FQDN is
            // exposed solely for OAuth start/callbacks. Flutter web assets deployed to SWA reference only MCP_CUSTOM_HOSTNAME.
            // Production fails closed on missing OIDC, keys, storage roles or MI. Single revision enforced.
            Configuration = new AppInputs.ConfigurationArgs
            {
                ActiveRevisionsMode = "Single",
                Ingress = new AppInputs.IngressArgs
                {
                    AllowInsecure = false,
                    External = true,
                    TargetPort = 8080,
                    Transport = "Auto"
                },
                Secrets =
                {
                    new AppInputs.SecretArgs { Name = SessionSigningKeySecret, Value = sessionSigningKey },
                    new AppInputs.SecretArgs { Name = DockerHubPasswordSecret, Value = dockerHubToken }
                },
                Registries =
                {
                    new AppInputs.RegistryCredentialsArgs
                    {
                        Server = "docker.io",
                        Username = dockerHubUsername,
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
                        Name = "mcp",
                        Image = mcpImage,
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
                            new AppInputs.EnvironmentVarArgs { Name = "ASPNETCORE_HTTP_PORTS", Value = "8080" },
                            new AppInputs.EnvironmentVarArgs { Name = "DIGITALBRAIN_ENV", Value = "cloud" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Profile", Value = "Production" },
                            new AppInputs.EnvironmentVarArgs { Name = "Aspire__Azure__Data__Tables__clustering__ServiceUri", Value = Output.Format($"https://{storage.Name}.table.core.windows.net") },
                            new AppInputs.EnvironmentVarArgs { Name = "Orleans__Clustering__ProviderType", Value = "AzureTableStorage" },
                            new AppInputs.EnvironmentVarArgs { Name = "Orleans__Clustering__ServiceKey", Value = "clustering" },
                            new AppInputs.EnvironmentVarArgs { Name = "Orleans__ClusterId", Value = "digitalbrain" },
                            new AppInputs.EnvironmentVarArgs { Name = "Orleans__ServiceId", Value = "digitalbrain" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Auth__SessionSigningKey", SecretRef = SessionSigningKeySecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__Ui__Oidc__Issuer", Value = oidcIssuer },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__Ui__Oidc__Audience", Value = oidcAudience },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__Ui__Oidc__AllowedGrants", Value = "brain.interact,brain.read,ui.action,gmail.read,salesforce.read" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__Mcp__Audience", Value = mcpAudience },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__Ui__Audience", Value = uiAudience },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__OAuth__InternalOrigin", Value = Output.Format($"https://{kernelApp.LatestRevisionFqdn}") },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__Mcp__AllowedOrigins", Value = frontendOrigins },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__Ui__AllowedOrigins", Value = frontendOrigins },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Runtime__ForwardedHeaders__TrustAzureContainerAppsIngress", Value = "true" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Google__RedirectUri", Value = googleRedirectUri },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Salesforce__RedirectUri", Value = salesforceRedirectUri },
                            new AppInputs.EnvironmentVarArgs { Name = "APPLICATIONINSIGHTS_CONNECTION_STRING", Value = appInsightsConnectionString }
                        }
                    }
                },
                // Runtime actions and authorization leases are deliberately single-owner. Do not autoscale
                // until their stores and workers have a verified multi-replica coordination protocol.
                Scale = new AppInputs.ScaleArgs { MinReplicas = 1, MaxReplicas = 1 },
                TerminationGracePeriodSeconds = 60
            },
            Tags = StandardTags("container-app-mcp")
        });

        var kernelPrincipalId = kernelApp.Identity.Apply(identity => identity!.PrincipalId!);
        var mcpPrincipalId = mcpApp.Identity.Apply(identity => identity!.PrincipalId!);
        GrantRole("kernel-storage-table-contributor", kernelPrincipalId, "0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3", storage.Id); // Storage Table Data Contributor
        GrantRole("kernel-storage-blob-contributor", kernelPrincipalId, "ba92f5b4-2d11-453d-a403-e96b0029c9fe", storage.Id); // Storage Blob Data Contributor
        GrantRole("mcp-storage-table-reader", mcpPrincipalId, "76199698-9eea-4c19-bc75-cec21354c6b6", storage.Id); // Storage Table Data Reader
        // Kernel identity isn't granted access until this deploys; the key-based path (openAiKey/OpenAiKeySecret
        // above) stays wired unchanged so DigitalBrainChat.cs's key branch keeps working until a verified,
        // separate follow-up deploy removes the key and flips DisableLocalAuth (Task 19 steps 2/4, out of scope here).
        GrantRole("kernel-openai-user", kernelPrincipalId, "5e0bd9bd-7b93-4f28-af87-19fc36ad61bd", openAi.Id); // Cognitive Services OpenAI User

        static void GrantRole(string resourceName, Input<string> principalId, string roleDefinitionGuid, Input<string> scope) =>
            _ = new Authorization.RoleAssignment(resourceName, new Authorization.RoleAssignmentArgs
            {
                PrincipalId = principalId,
                PrincipalType = Authorization.PrincipalType.ServicePrincipal,
                RoleDefinitionId = $"/providers/Microsoft.Authorization/roleDefinitions/{roleDefinitionGuid}",
                Scope = scope
            });

        return new Dictionary<string, object?>
        {
            ["resourceGroup"] = resourceGroup.Name,
            ["storageAccount"] = storage.Name,
            ["openAiEndpoint"] = openAiEndpoint,
            ["chatDeployment"] = ChatDeploymentName,
            ["kernelApp"] = kernelApp.Name,
            ["kernelFqdn"] = kernelApp.LatestRevisionFqdn,
            ["kernelCustomEndpoint"] = kernelCustomEndpoint,
            ["mcpApp"] = mcpApp.Name,
            ["mcpFqdn"] = mcpApp.LatestRevisionFqdn,
            ["mcpCustomEndpoint"] = mcpCustomEndpoint,
            ["imageTag"] = imageTag,
            ["environment"] = EnvSuffix
        };
    }

    private static string ConfiguredHttpsOrigin(Config config, string envName, string configName, string defaultHostname)
    {
        var configured = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = config.Get(configName);
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = defaultHostname;
        }

        var trimmed = configured.Trim().TrimEnd('/');
        var candidate = trimmed.Contains("://", System.StringComparison.Ordinal)
            ? trimmed
            : $"https://{trimmed}";
        if (!System.Uri.TryCreate(candidate, System.UriKind.Absolute, out var origin) ||
            !string.Equals(origin.Scheme, System.Uri.UriSchemeHttps, System.StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(origin.Host) ||
            origin.UserInfo.Length != 0 ||
            origin.AbsolutePath is not ("" or "/") ||
            origin.Query.Length != 0 ||
            origin.Fragment.Length != 0)
        {
            throw new System.InvalidOperationException(
                $"Production origin {envName} must be an HTTPS authority without credentials, path, query, or fragment.");
        }

        return origin.GetLeftPart(System.UriPartial.Authority);
    }

    private static string RequiredSetting(Config config, string configName, string envName)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(value)) value = config.Get(configName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new System.InvalidOperationException(
                $"Required production setting is missing: set {envName} or Pulumi config '{configName}'.");
        }

        return value.Trim();
    }

    private static string RequiredHttpsUri(Config config, string configName, string envName)
    {
        var value = RequiredSetting(config, configName, envName);
        if (!System.Uri.TryCreate(value, System.UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, System.Uri.UriSchemeHttps, System.StringComparison.OrdinalIgnoreCase))
        {
            throw new System.InvalidOperationException(
                $"Required production setting {envName} must be an absolute HTTPS URI.");
        }

        return value;
    }

    private static Output<string> RequiredSecret(Config config, string configName, string envName)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value)) return Output.CreateSecret(value);

        var configured = config.GetSecret(configName);
        if (configured is not null) return configured;

        throw new System.InvalidOperationException(
            $"Required production secret is missing: set {envName} or encrypted Pulumi config '{configName}'.");
    }

    private static void DemandExactSetting(string actual, string expected, string envName)
    {
        if (!string.Equals(actual, expected, System.StringComparison.Ordinal))
            throw new System.InvalidOperationException($"Production setting {envName} must be '{expected}'.");
    }

    private static void DemandOAuthCallback(string actual, string kernelOrigin, string provider, string envName)
    {
        var expected = $"{kernelOrigin.TrimEnd('/')}/oauth/callback/{provider}";
        if (!string.Equals(actual.TrimEnd('/'), expected, System.StringComparison.OrdinalIgnoreCase))
        {
            throw new System.InvalidOperationException(
                $"Production setting {envName} must target the kernel's bounded OAuth callback '{expected}'.");
        }
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
