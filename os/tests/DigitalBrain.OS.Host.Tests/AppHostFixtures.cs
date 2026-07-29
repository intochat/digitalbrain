using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Testing;
using Xunit;

[assembly: AssemblyFixture(typeof(DigitalBrain.HostTests.TestingAppHostFixture))]

namespace DigitalBrain.HostTests;

[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit assembly fixture types must be public.")]
public sealed class TestingAppHostFixture :
    DigitalBrainAppHostFixture<Projects.DigitalBrain_TestingAppHost>
{
    public const string SiloResourceName = "silo";

    public const string BehaviorHostResourceName = "behavior-host";

    public const string HealthPath = "/health";
}
