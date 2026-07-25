using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Testing;
using Xunit;

[assembly: AssemblyFixture(typeof(DigitalBrain.HostTests.TestingAppHostFixture))]
[assembly: AssemblyFixture(typeof(DigitalBrain.HostTests.QuickstartAppHostFixture))]

namespace DigitalBrain.HostTests;

[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit assembly fixture types must be public.")]
public sealed class TestingAppHostFixture :
    DigitalBrainAppHostFixture<Projects.DigitalBrain_TestingAppHost>;

[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit assembly fixture types must be public.")]
public sealed class QuickstartAppHostFixture :
    DigitalBrainAppHostFixture<Projects.DigitalBrain_Quickstart_AppHost>;
