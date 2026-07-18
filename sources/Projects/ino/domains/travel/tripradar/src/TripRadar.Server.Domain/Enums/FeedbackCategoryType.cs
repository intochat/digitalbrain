using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class FeedbackCategoryType(int id, string name) : Enumeration(id, name)
{
    public static readonly FeedbackCategoryType General = new(1, nameof(General));
    public static readonly FeedbackCategoryType BugReport = new(2, nameof(BugReport));
    public static readonly FeedbackCategoryType FeatureRequest = new(3, nameof(FeatureRequest));
    public static readonly FeedbackCategoryType Performance = new(4, nameof(Performance));
    public static readonly FeedbackCategoryType UserInterface = new(5, nameof(UserInterface));
    public static readonly FeedbackCategoryType Documentation = new(6, nameof(Documentation));
    public static readonly FeedbackCategoryType SubscriptionCancellation = new(7, nameof(SubscriptionCancellation));
}
