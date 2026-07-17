using Brain.KernelTests;
using Brain.Modules.Sdk;

namespace Brain.ConformanceTests;

public sealed class SalesforceConformance(BrainClusterFixture<ConnectorsKindsConfigurator> fixture)
    : KindConformance<ConnectorsKindsConfigurator>(fixture)
{
    protected override string KindName => "salesforce";
    protected override string SampleContract => "salesforce.propose-update.v1";
    protected override string SampleInputJson => """{"objectId":"acc-1","fields":{"Name":"Acme"}}""";
}
