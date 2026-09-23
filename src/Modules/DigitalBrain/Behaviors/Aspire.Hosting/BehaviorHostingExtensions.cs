using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Behavior.Aspire.Hosting;

public static class BehaviorHostingExtensions
{
    public static DigitalBrainModuleBuilder<BehaviorModule> WithLocalExecution(this DigitalBrainModuleBuilder<BehaviorModule> module, string root)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        module.AddProjection(new ExecutionProjection(Path.GetFullPath(root)));
        return module;
    }

    private sealed class ExecutionProjection(string root) : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            builder.WithEnvironment("DigitalBrain__Behavior__Root", Path.Combine(root, "behaviors"));
            builder.WithEnvironment("DigitalBrain__Coding__Execution__Root", Path.Combine(root, "coding"));
        }
    }
}