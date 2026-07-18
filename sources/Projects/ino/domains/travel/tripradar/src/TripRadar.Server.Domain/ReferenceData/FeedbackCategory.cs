namespace TripRadar.Server.Domain.ReferenceData;

public class FeedbackCategory
{
    private FeedbackCategory()
    {
    }

    public FeedbackCategory(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Feedback category name is required", nameof(name));
        }
        Name = name.Trim();
    }

    public string Name { get; } = null!;
}
