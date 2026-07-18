using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.SeedWork;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Domain.Entities;

public class Feedback : Entity<long>
{
    private Feedback()
    {
    }

    public Feedback(long userId, string title, string content, int rating, FeedbackCategoryType categoryType)
    {
        UserId = userId;
        Title = title;
        Content = content;
        Rating = rating;
        CategoryId = categoryType.Id;
        CreatedOn = DateTime.UtcNow;
    }

    public new long Id { get; private set; }

    public long UserId { get; private set; }

    public User User { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string Content { get; private set; } = null!;

    public int Rating { get; private set; }

    public int CategoryId { get; private set; }

    public FeedbackCategory Category { get; private set; } = null!;

    public DateTime CreatedOn { get; private set; }

    public DateTime? UpdatedOn { get; private set; }
}

