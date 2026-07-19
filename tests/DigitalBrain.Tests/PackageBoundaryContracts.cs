using System.Reflection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class PackageBoundaryContracts
{
    private static readonly string[] ProviderSdks = ["OpenAI", "Anthropic"];

    public static TheoryData<string> PackagesThatMustNotSeeProviderSdks { get; } = new(
        "DigitalBrain.Abstractions",
        "DigitalBrain.Client",
        "DigitalBrain.Testing",
        "DigitalBrain.Aspire",
        "DigitalBrain.Aspire.Hosting");

    [Theory]
    [MemberData(nameof(PackagesThatMustNotSeeProviderSdks))]
    public void ProviderSdksLiveOnlyInTheKernel(string assemblyName)
    {
        var referenced = Assembly.Load(assemblyName)
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => ProviderSdks.Any(sdk => name.StartsWith(sdk, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(referenced);
    }

    [Fact]
    public void TheClientNeverReferencesTheRuntime()
    {
        var referenced = Assembly.Load("DigitalBrain.Client")
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToList();

        Assert.DoesNotContain("DigitalBrain.Kernel", referenced, StringComparer.Ordinal);
    }
}
