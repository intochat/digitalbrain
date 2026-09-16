using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Supabase;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModuleHostingHierarchyFacts
{
    [Fact]
    public async Task Storage_nests_under_Kernel_under_Modules()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        builder.AddDigitalBrain("Modules").AddModule<SupabaseModule>();

        Assert.Equal("Modules", ParentName(builder.Resources.OfType<DigitalBrainModuleResource>().Single(resource => resource.Name == "Kernel")));
        Assert.Equal("Kernel", ParentName(builder.Resources.Single(resource => resource.Name == "storage")));
        await using var app = builder.Build();
    }

    [Fact]
    public async Task Supabase_connection_is_the_Supabase_module_resource()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var brain = builder.AddDigitalBrain("Modules").AddModule<SupabaseModule>();
        builder.AddExecutable("consumer", "unused", ".").WithReference(brain);
        var supabase = Assert.Single(builder.Resources, resource => resource.Name == "Supabase");
        Assert.Equal("Modules", ParentName(supabase));
        await using var app = builder.Build();
    }

    private static string? ParentName(IResource resource)
        => resource.Annotations
            .OfType<ResourceRelationshipAnnotation>()
            .FirstOrDefault(annotation => annotation.Type == "Parent")
            ?.Resource.Name;
}
