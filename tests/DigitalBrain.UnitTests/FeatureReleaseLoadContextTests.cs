using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DigitalBrain.FeatureHost;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Kernel.Contracts;
using Xunit;

namespace DigitalBrain.UnitTests;

public sealed class FeatureReleaseLoadContextTests
{
    [Fact]
    public void Nonshared_host_assemblies_cannot_fall_back_to_the_default_context()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        var context = new FeatureReleaseLoadContext(
            release.ImplementationAssemblyPath,
            [typeof(IFeature).Assembly, typeof(IGmailMessageReader).Assembly]);
        try
        {
            Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(typeof(ReleaseDigest).Assembly));
            var loader = typeof(FeatureReleaseLoadContext).GetMethod(
                "Load",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
            var exception = Assert.Throws<TargetInvocationException>(() =>
                loader.Invoke(context, [typeof(ReleaseDigest).Assembly.GetName()]));
            Assert.IsType<FileNotFoundException>(exception.InnerException);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Sdk_and_integration_contracts_keep_default_context_type_identity()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        var reference = AssertIdentityAndUnload(release.ImplementationAssemblyPath);

        Collect(reference);
        Assert.False(reference.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AssertIdentityAndUnload(string implementationAssemblyPath)
    {
        var context = new FeatureReleaseLoadContext(
            implementationAssemblyPath,
            [typeof(IFeature).Assembly, typeof(IGmailMessageReader).Assembly]);
        try
        {
            var assembly = context.LoadFromAssemblyPath(implementationAssemblyPath);
            var featureType = assembly.GetType(
                "DigitalBrain.Features.EmailSummarizer.EmailSummarizerFeature",
                throwOnError: true)!;

            Assert.True(context.IsCollectible);
            Assert.Same(context, AssemblyLoadContext.GetLoadContext(assembly));
            Assert.True(typeof(IFeature).IsAssignableFrom(featureType));
            Assert.Equal(typeof(IGmailMessageReader), Assert.Single(featureType.GetConstructors()).GetParameters()[0].ParameterType);
            Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(featureType.GetInterfaces().Single().Assembly));
        }
        finally
        {
            context.Unload();
        }

        return new WeakReference(context, trackResurrection: true);
    }

    [Fact]
    public async Task Concurrent_resolution_returns_one_cached_implementation_assembly()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        var reference = await AssertConcurrentResolutionAndUnload(release.ImplementationAssemblyPath);

        Collect(reference);
        Assert.False(reference.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> AssertConcurrentResolutionAndUnload(string implementationAssemblyPath)
    {
        var context = new FeatureReleaseLoadContext(
            implementationAssemblyPath,
            [typeof(IFeature).Assembly, typeof(IGmailMessageReader).Assembly]);
        try
        {
            var name = AssemblyName.GetAssemblyName(implementationAssemblyPath);
            var assemblies = await Task.WhenAll(Enumerable.Range(0, 32)
                .Select(_ => Task.Run(() => context.LoadFromAssemblyName(name))));

            Assert.All(assemblies, assembly => Assert.Same(assemblies[0], assembly));
            Assert.Single(context.Assemblies, assembly => assembly.GetName().Name == name.Name);
        }
        finally
        {
            context.Unload();
        }

        return new WeakReference(context, trackResurrection: true);
    }

    [Fact]
    public void Unload_releases_the_collectible_context()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        var reference = LoadAndUnload(release.ImplementationAssemblyPath);

        Collect(reference);

        Assert.False(reference.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndUnload(string implementationAssemblyPath)
    {
        var context = new FeatureReleaseLoadContext(
            implementationAssemblyPath,
            [typeof(IFeature).Assembly, typeof(IGmailMessageReader).Assembly]);
        _ = context.LoadFromAssemblyPath(implementationAssemblyPath)
            .GetType("DigitalBrain.Features.EmailSummarizer.EmailSummarizerFeature", throwOnError: true);
        var reference = new WeakReference(context, trackResurrection: true);
        context.Unload();
        return reference;
    }

    private static void Collect(WeakReference reference)
    {
        for (var attempt = 0; reference.IsAlive && attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

}
