using DigitalBrain.Coding;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CodeTestReportFacts
{
    [Fact]
    public void AConformanceTestAloneIsNotUserEvidence()
        => Assert.Throws<InvalidDataException>(() => CodeTestReportReader.Read("<assemblies><assembly><collection><test name=\"DigitalBrainConformance.EntryPoint\" result=\"Pass\" /></collection></assembly></assemblies>"));

    [Fact]
    public void AllTestsAndHostConformanceMustPass()
    {
        var report = CodeTestReportReader.Read("<assemblies><assembly><collection><test name=\"DigitalBrainConformance.EntryPoint\" result=\"Pass\" /><test name=\"UserTests.Works\" result=\"Pass\" /></collection></assembly></assemblies>");
        Assert.Equal(2, report.Passed);
    }

    [Fact]
    public void SkippedTestsAreNotPassingEvidence()
        => Assert.Throws<InvalidDataException>(() => CodeTestReportReader.Read("<assemblies><assembly><collection><test name=\"DigitalBrainConformance.EntryPoint\" result=\"Pass\" /><test name=\"UserTests.Works\" result=\"Skip\" /></collection></assembly></assemblies>"));
}