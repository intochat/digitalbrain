using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class UserSubscription : Entity<long>
{
    public UserSubscription()
    {
    }

    public UserSubscription(User user)
    {
        User = user;
        UserId = user.Id;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        user.AttachSubscription(this);
    }

    public User User { get; private set; } = null!;

    public long UserId { get; private set; }

    public string? StripeCustomerId { get; private set; }

    public string? StripeSubscriptionId { get; private set; }

    public DateTime? SubscriptionExpirationTime { get; private set; }

    public Tier? PendingTier { get; set; }

    public int? PendingTierId { get; private set; }

    public bool IsActive { get; private set; } = true;

    public bool PayAsYouGoEnabled { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; private set; }

    public string? DeferredDowngradeJobId { get; private set; }

    public bool CanUseOverage(bool requiresStripeSubscriptionId)
    {
        if (!PayAsYouGoEnabled || !IsActive)
        {
            return false;
        }

        return !requiresStripeSubscriptionId || !string.IsNullOrWhiteSpace(StripeSubscriptionId);
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        PayAsYouGoEnabled = false;
        PendingTier = null;
        PendingTierId = null;
        DeferredDowngradeJobId = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateStripeSubscriptionId(string? stripeSubscriptionId)
    {
        StripeSubscriptionId = stripeSubscriptionId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateStripeCustomerId(string? stripeCustomerId)
    {
        StripeCustomerId = stripeCustomerId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateSubscriptionExpirationTime(DateTime? subscriptionExpirationTime)
    {
        SubscriptionExpirationTime = subscriptionExpirationTime;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdatePendingTier(Tier? pendingTier)
    {
        PendingTier = pendingTier;
        PendingTierId = pendingTier?.Id;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDeferredDowngrade(DateTime? expirationTime, int? targetTierId)
    {
        SubscriptionExpirationTime = expirationTime;
        PendingTierId = targetTierId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetDeferredDowngradeJobId(string? jobId)
    {
        DeferredDowngradeJobId = jobId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ExtendSubscription(DateTime additionalTime)
    {
        SubscriptionExpirationTime = SubscriptionExpirationTime?.Add(additionalTime - DateTime.UtcNow) ?? additionalTime;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetPayAsYouGo(bool enabled)
    {
        PayAsYouGoEnabled = enabled;
        UpdatedAt = DateTime.UtcNow;
    }
}
