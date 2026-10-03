using DigitalBrain.Deployment;
using Pulumi;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;
using Pulumi.AzureNative.Network;
using Pulumi.Random;
using NetworkInputs = Pulumi.AzureNative.Network.Inputs;

namespace DigitalBrain.Microsoft.CSharp;

// Scripts run in a custom-container session pool: one Hyper-V session per owner running the Sandbox
// image. The pool has its own environment and subnet, whose NSG lets sessions out on HTTPS only (the
// script edge and package restores); limiting that to the edge alone needs Azure Firewall rules.
public sealed class CSharpDeployment : IDigitalBrainModuleDeployment
{
    // Settings: "sandboxImage" (the Sandbox image in the registry) and "sessionExecutorRoleId", the id of
    // the built-in "Azure ContainerApps Session Executor" role:
    // az role definition list --name "Azure ContainerApps Session Executor" --query "[0].name" -o tsv
    public void Deploy(ModuleDeploymentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var foundation = context.Foundation;
        var name = foundation.NamingPrefix + "-sandbox";
        var outboundHttpsOnly = new NetworkSecurityGroup(name, new NetworkSecurityGroupArgs
        {
            NetworkSecurityGroupName = name,
            ResourceGroupName = foundation.ResourceGroupName,
            Location = foundation.Location,
            SecurityRules =
            [
                new NetworkInputs.SecurityRuleArgs
                {
                    Name = "allow-https-out", Priority = 100, Direction = SecurityRuleDirection.Outbound, Access = SecurityRuleAccess.Allow,
                    Protocol = SecurityRuleProtocol.Tcp, SourceAddressPrefix = "*", SourcePortRange = "*", DestinationAddressPrefix = "*", DestinationPortRange = "443",
                },
                new NetworkInputs.SecurityRuleArgs
                {
                    Name = "deny-other-out", Priority = 4000, Direction = SecurityRuleDirection.Outbound, Access = SecurityRuleAccess.Deny,
                    Protocol = SecurityRuleProtocol.Asterisk, SourceAddressPrefix = "*", SourcePortRange = "*", DestinationAddressPrefix = "Internet", DestinationPortRange = "*",
                },
            ],
        });
        var subnet = new Subnet(name, new SubnetArgs
        {
            SubnetName = name,
            ResourceGroupName = foundation.ResourceGroupName,
            VirtualNetworkName = foundation.VirtualNetworkName,
            AddressPrefix = context.Settings.Get("sandboxSubnet") ?? "10.0.10.0/23",
            NetworkSecurityGroup = new NetworkInputs.NetworkSecurityGroupArgs { Id = outboundHttpsOnly.Id },
            Delegations = [new NetworkInputs.DelegationArgs { Name = "sandbox", ServiceName = "Microsoft.App/environments" }],
        });
        var environment = new ManagedEnvironment(name, new ManagedEnvironmentArgs
        {
            EnvironmentName = name,
            ResourceGroupName = foundation.ResourceGroupName,
            Location = foundation.Location,
            WorkloadProfiles = [new WorkloadProfileArgs { Name = "Consumption", WorkloadProfileType = "Consumption" }],
            VnetConfiguration = new VnetConfigurationArgs { InfrastructureSubnetId = subnet.Id, Internal = false },
        });
        var pool = new ContainerAppsSessionPool(name, new ContainerAppsSessionPoolArgs
        {
            SessionPoolName = name,
            ResourceGroupName = foundation.ResourceGroupName,
            Location = foundation.Location,
            EnvironmentId = environment.Id,
            ContainerType = ContainerType.CustomContainer,
            PoolManagementType = PoolManagementType.Dynamic,
            // The pool pulls the image as the runtime identity, which already holds AcrPull; sessions never see it.
            Identity = new Pulumi.AzureNative.App.Inputs.ManagedServiceIdentityArgs
            {
                Type = Pulumi.AzureNative.App.ManagedServiceIdentityType.UserAssigned,
                UserAssignedIdentities = [foundation.RuntimeIdentityId],
            },
            ManagedIdentitySettings = [new ManagedIdentitySettingArgs { Identity = foundation.RuntimeIdentityId, Lifecycle = IdentitySettingsLifeCycle.None }],
            CustomContainerTemplate = new CustomContainerTemplateArgs
            {
                Containers =
                [
                    new SessionContainerArgs
                    {
                        Name = "sandbox",
                        Image = context.Settings.Require("sandboxImage"),
                        Resources = new SessionContainerResourcesArgs { Cpu = 1, Memory = "2Gi" },
                        // A session with no runs exits and frees itself; the pool keeps a session until its container exits.
                        Env = [new Pulumi.AzureNative.App.Inputs.EnvironmentVarArgs { Name = "Sandbox__IdleShutdown", Value = "00:05:00" }],
                    },
                ],
                Ingress = new SessionIngressArgs { TargetPort = CSharpSandbox.Port },
                RegistryCredentials = new SessionRegistryCredentialsArgs { Server = foundation.RegistryLoginServer, Identity = foundation.RuntimeIdentityId },
            },
            DynamicPoolConfiguration = new DynamicPoolConfigurationArgs
            {
                LifecycleConfiguration = new LifecycleConfigurationArgs { LifecycleType = LifecycleType.OnContainerExit, MaxAlivePeriodInSeconds = 86400 },
            },
            ScaleConfiguration = new ScaleConfigurationArgs { MaxConcurrentSessions = 20, ReadySessionInstances = 1 },
            SessionNetworkConfiguration = new SessionNetworkConfigurationArgs { Status = SessionNetworkStatus.EgressEnabled },
        }, new CustomResourceOptions { DependsOn = [.. context.RuntimeGrants] });
        context.GrantRuntime("sandbox-sessions", context.Settings.Require("sessionExecutorRoleId"), pool.Id);

        // Run tokens must verify on every silo, so the key is generated once and kept in state and Key Vault.
        var runTokenKey = new RandomId(name + "-run-token-key", new RandomIdArgs { ByteLength = 32 },
            new CustomResourceOptions { AdditionalSecretOutputs = ["hex", "b64Std", "b64Url", "dec"] });
        context.SetRuntimeEnvironment("DigitalBrain__CSharp__SessionPoolEndpoint", pool.PoolManagementEndpoint);
        context.SetRuntimeEnvironment("DigitalBrain__CSharp__EdgeUrl", foundation.RuntimeUrl);
        context.SetRuntimeEnvironment("DigitalBrain__CSharp__RunTokenKey", runTokenKey.B64Std, secret: true);
    }
}
