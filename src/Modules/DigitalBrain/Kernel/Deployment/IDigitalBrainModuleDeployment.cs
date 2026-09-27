namespace DigitalBrain.Deployment;

// A module's cloud deployment, named by [ModuleDeployment] on the module. It asks the DeploymentKit
// foundation for what it needs, then deploys the Azure resources behind the manifest resources it
// owns and provides the values the brain runtime's environment reads from them.
public interface IDigitalBrainModuleDeployment
{
    void ConfigureFoundation(FoundationContext context) { }

    void Deploy(ModuleDeploymentContext context);
}
