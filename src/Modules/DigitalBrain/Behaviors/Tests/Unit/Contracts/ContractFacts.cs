using System.Text.Json;
using DigitalBrain.Behavior;
using DigitalBrain.Coding;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ContractFacts
{
    [Fact]
    public void WireContractsRetainEveryValue()
    {
        using var services = new ServiceCollection().AddSerializer().BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer>();
        var operation = Guid.NewGuid();
        var artifact = new CodeArtifactRef("artifact", "source", "environment");
        var now = DateTimeOffset.UtcNow;
        object[] values =
        [
            artifact, new SaveCodeDraft(3, operation, "source", "tests", ["time"]), new CheckCodeDraft(3, operation),
            new CodeDraftSnapshot(3, "source", "tests", ["time"], operation),
            new CodeCheckSnapshot(operation, 3, CodeCheckStatus.Passed,
                [new("C1", "warning", "message", "Program.cs", 1, 2)], new(1, 1, 0, 0), now, now, artifact),
            new CodeDraftSaved("draft", 3), new CodeCheckChanged("draft", operation, 3, CodeCheckStatus.Passed),
            new DeployBehavior(2, operation, artifact, "{}", operation), new ChangeBehaviorState(2, operation),
            new RollbackBehavior(2, operation, 1),
            new BehaviorSnapshot(2, BehaviorDesiredState.Running, BehaviorExecutionState.Running, 1, 1, operation,
                true, null, [new(1, artifact, "{}", now, operation)]),
            new BehaviorSnapshot(0, BehaviorDesiredState.Stopped, BehaviorExecutionState.Stopped, null, null, null, false, null, []),
            new BehaviorLogPage([new(1, operation, now, "stdout", "hello")], 1, false),
            new BehaviorDeploymentChanged("program", 2, "artifact"),
            new BehaviorExecutionChanged("program", operation, BehaviorExecutionState.Failed, false, "failure"),
            new BehaviorLogAvailable("program", operation, 1),
        ];
        foreach (var value in values)
        {
            var copy = serializer.Deserialize<object>(serializer.SerializeToArray(value));
            Assert.NotNull(copy);
            Assert.Equal(value.GetType(), copy.GetType());
            Assert.Equal(JsonSerializer.Serialize(value), JsonSerializer.Serialize(copy));
        }
    }
}