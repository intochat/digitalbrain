using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brain.KernelTests;
using Brain.Modules.Sdk;

namespace Brain.ConformanceTests;

public sealed class BehaviorConformance(BrainClusterFixture<BehaviorsKindsConfigurator> fixture)
    : KindConformance<BehaviorsKindsConfigurator>(fixture)
{
    private const string SampleSource = "conformance behavior source";
    private static readonly string SampleSourceHash =
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SampleSource)));

    protected override string KindName => "behavior";
    protected override string SampleContract => "behavior.propose.v1";

    protected override string SampleInputJson => JsonSerializer.Serialize(new
    {
        source = SampleSource,
        sourceHash = SampleSourceHash,
        bddPassed = true,
        grants = Array.Empty<object>()
    });
}
