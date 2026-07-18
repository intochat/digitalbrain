using Aspire.Hosting.ApplicationModel;
using DigitalBrain;

namespace Aspire.Hosting.DigitalBrain;

[AspireExportIgnore(Reason = "Typed .NET model descriptors are not ATS generic arguments.")]
public sealed class DigitalBrainChatModelBuilder
{
    internal DigitalBrainChatModelBuilder(
        IResourceBuilder<DigitalBrainResource> brain,
        ChatModelDescriptor descriptor)
    {
        Brain = brain;
        Descriptor = descriptor;
    }

    internal IResourceBuilder<DigitalBrainResource> Brain { get; }

    internal ChatModelDescriptor Descriptor { get; }

    public IResourceBuilder<DigitalBrainResource> AsFast()
    {
        Brain.Resource.AssignFast(Descriptor);
        return Brain;
    }

    public IResourceBuilder<DigitalBrainResource> AsBalanced()
    {
        Brain.Resource.AssignBalanced(Descriptor);
        return Brain;
    }

    public IResourceBuilder<DigitalBrainResource> AsReasoning()
    {
        Brain.Resource.AssignReasoning(Descriptor);
        return Brain;
    }
}
