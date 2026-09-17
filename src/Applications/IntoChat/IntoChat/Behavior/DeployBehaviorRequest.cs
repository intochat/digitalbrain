using DigitalBrain.Abstractions.Behavior;

namespace IntoChat;

internal sealed record DeployBehaviorRequest(BehaviorDefinition Definition, long? ExpectedVersion = null);
