using Brain.Abstractions.Activities;
using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Operations;
using Xunit;

namespace Brain.Abstractions.Tests;

public sealed class ContractCategoryTests
{
    [Fact]
    public void Operation_descriptor_requires_distinct_input_and_result_contracts()
    {
        var input = new ContractId("proof/run-input@1");

        Assert.Throws<ArgumentException>(() => new OperationDescriptor(
            new OperationId("proof.run"),
            input,
            input,
            new NeuronRoleId("proof.entry"),
            new ModuleId("proof"),
            new ContractVersion(1)));
    }

    [Fact]
    public void Activity_view_contains_status_and_redacted_contract_references_not_raw_event_payloads()
    {
        var view = ActivityView.Accepted(
            BrainActivityId.New(),
            new OperationId("proof.run"),
            new ContractId("proof/run-result@1"));

        Assert.Equal(ActivityStatus.Accepted, view.Status);
        Assert.DoesNotContain(
            typeof(ActivityView).GetProperties(),
            property => property.Name.Contains("Journal", StringComparison.Ordinal));
    }

    [Fact]
    public void Event_descriptor_requires_a_concrete_domain_event_type()
    {
        Assert.Throws<ArgumentException>(() => new EventDescriptor(
            new ContractId("proof/run-completed@1"),
            new ModuleId("proof"),
            typeof(NotADomainEvent),
            EventVisibility.Internal));
    }

    [Fact]
    public void Capability_descriptor_rejects_a_default_contract_version()
    {
        Assert.Throws<ArgumentException>(() => new CapabilityDescriptor(
            new CapabilityId("proof.execute"),
            new ContractId("proof/execute-request@1"),
            new ContractId("proof/execute-result@1"),
            new ModuleId("proof"),
            default));
    }

    [Fact]
    public void Activity_references_contain_only_contract_and_opaque_payload_reference()
    {
        var properties = typeof(ActivityResultReference).GetProperties();

        Assert.Equal(
            [nameof(ActivityResultReference.Contract), nameof(ActivityResultReference.Payload)],
            properties.Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    private sealed class NotADomainEvent;
}
