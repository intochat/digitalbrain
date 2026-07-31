using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

[GenerateSerializer]
[Alias("db.behaviors.user-action-required-exception")]
public sealed class BehaviorUserActionRequiredException : Exception
{
    public BehaviorUserActionRequiredException()
        : this("A module user action is required before the operation can continue.")
    {
    }

    public BehaviorUserActionRequiredException(string message)
        : base(message)
    {
    }

    public BehaviorUserActionRequiredException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public BehaviorUserActionRequiredException(UserActionRequired requirement)
        : base(BuildMessage(requirement))
    {
        ArgumentNullException.ThrowIfNull(requirement);
        Requirement = requirement;
    }

    [Id(0)]
    public UserActionRequired? Requirement { get; set; }

    private static string BuildMessage(UserActionRequired requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return $"{requirement.ModuleId} requires user action before the operation can continue.";
    }
}
