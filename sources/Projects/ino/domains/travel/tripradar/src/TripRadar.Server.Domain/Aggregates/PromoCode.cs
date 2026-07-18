using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Events;
using TripRadar.Server.Domain.Rules;
using TripRadar.Server.Domain.SeedWork;
using DomainDiscountType = TripRadar.Server.Domain.Enums.DiscountType;

namespace TripRadar.Server.Domain.Aggregates;

public class PromoCode : AggregateRoot<long>
{
    private PromoCode()
    {
        Code = string.Empty;
        CurrentUsageCount = 0;
        MaxUsagePerUser = 1;
        IsActive = true;
        IsDeleted = false;
        CreatedAt = DateTime.UtcNow;
    }

    public PromoCode(
        string code,
        string? description,
        int discountTypeId,
        decimal discountValue,
        int? maxUsageCount,
        int maxUsagePerUser,
        DateTime startDate,
        DateTime endDate,
        bool isActive = true,
        bool isDeleted = false)
    {
        ValidateConfiguration(discountTypeId, discountValue, maxUsageCount, maxUsagePerUser, startDate, endDate);

        Code = NormalizeRequired(code, nameof(code));
        Description = NormalizeOptional(description);
        DiscountTypeId = discountTypeId;
        DiscountValue = discountValue;
        MaxUsageCount = maxUsageCount;
        CurrentUsageCount = 0;
        MaxUsagePerUser = maxUsagePerUser;
        StartDate = NormalizeUtc(startDate);
        EndDate = NormalizeUtc(endDate);
        IsActive = isActive;
        IsDeleted = isDeleted;
        CreatedAt = DateTime.UtcNow;
    }

    public new long Id { get; private set; }

    public string Code { get; private set; } = null!;

    public string? Description { get; private set; }

    public int DiscountTypeId { get; private set; }

    public decimal DiscountValue { get; private set; }

    public int? MaxUsageCount { get; private set; }

    public int CurrentUsageCount { get; private set; }

    public int MaxUsagePerUser { get; private set; } = 1;

    public DateTime StartDate { get; private set; }

    public DateTime EndDate { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    public bool IsDeleted { get; private set; }

    public DiscountType? DiscountType { get; private set; }

    private ICollection<PromoCodeUsage> PromoCodeUsages { get; set; } = new List<PromoCodeUsage>();

    private ICollection<User> Users { get; set; } = new List<User>();

    public DomainResult<decimal> Apply(long userId, decimal orderAmount, int userUsageCount, DateTime? utcNow = null)
    {
        var now = NormalizeUtc(utcNow ?? DateTime.UtcNow);

        if (!IsActive)
        {
            return DomainResult.Failure<decimal>(DomainErrors.PromoCodeInactive);
        }

        if (now < StartDate)
        {
            return DomainResult.Failure<decimal>(DomainErrors.PromoCodeNotStarted);
        }

        if (IsExpired(now))
        {
            return DomainResult.Failure<decimal>(DomainErrors.PromoCodeExpired);
        }

        if (HasReachedMaxUsage())
        {
            return DomainResult.Failure<decimal>(DomainErrors.PromoCodeUsageLimitExceeded);
        }

        if (userUsageCount >= MaxUsagePerUser)
        {
            return DomainResult.Failure<decimal>(DomainErrors.PromoCodeAlreadyUsedByUser);
        }

        var discountAmount = CalculateDiscount(orderAmount);
        CurrentUsageCount++;
        UpdatedAt = now;
        RaiseDomainEvent(new PromoCodeAppliedDomainEvent(Id, Code, userId, discountAmount));

        return DomainResult.Success(discountAmount);
    }

    public bool IsExpired(DateTime? utcNow = null) => NormalizeUtc(utcNow ?? DateTime.UtcNow) > EndDate;

    public bool IsNotStarted(DateTime? utcNow = null) => NormalizeUtc(utcNow ?? DateTime.UtcNow) < StartDate;

    public bool HasReachedMaxUsage() => MaxUsageCount.HasValue && CurrentUsageCount >= MaxUsageCount.Value;

    public void IncrementUsageCount()
    {
        CurrentUsageCount++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(string? description, int? maxUsageCount, int? maxUsagePerUser, DateTime? startDate, DateTime? endDate, bool? isActive)
    {
        var updatedMaxUsageCount = maxUsageCount ?? MaxUsageCount;
        var updatedMaxUsagePerUser = maxUsagePerUser ?? MaxUsagePerUser;
        var updatedStartDate = startDate ?? StartDate;
        var updatedEndDate = endDate ?? EndDate;

        ValidateConfiguration(DiscountTypeId, DiscountValue, updatedMaxUsageCount, updatedMaxUsagePerUser, updatedStartDate, updatedEndDate);

        if (description != null)
        {
            Description = NormalizeOptional(description);
        }

        MaxUsageCount = updatedMaxUsageCount;
        MaxUsagePerUser = updatedMaxUsagePerUser;
        StartDate = NormalizeUtc(updatedStartDate);
        EndDate = NormalizeUtc(updatedEndDate);

        if (isActive.HasValue)
        {
            IsActive = isActive.Value;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsDeleted()
    {
        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
    }

    private decimal CalculateDiscount(decimal orderAmount)
    {
        var discountAmount = DiscountTypeId == DomainDiscountType.Percentage.Id
            ? orderAmount * (DiscountValue / 100m)
            : DiscountValue;

        return Math.Min(discountAmount, orderAmount);
    }

    private static void ValidateConfiguration(
        int discountTypeId,
        decimal discountValue,
        int? maxUsageCount,
        int maxUsagePerUser,
        DateTime startDate,
        DateTime endDate)
    {
        if (discountTypeId != DomainDiscountType.Percentage.Id && discountTypeId != DomainDiscountType.FixedAmount.Id)
        {
            throw new ArgumentException("Invalid discount type.", nameof(discountTypeId));
        }

        if (discountTypeId == DomainDiscountType.Percentage.Id && (discountValue <= 0 || discountValue > 100))
        {
            throw new ArgumentOutOfRangeException(nameof(discountValue), "Percentage discount must be between 0 and 100.");
        }

        if (discountTypeId == DomainDiscountType.FixedAmount.Id && discountValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(discountValue), "Fixed amount discount must be greater than 0.");
        }

        if (maxUsageCount is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUsageCount), "Max usage count must be greater than 0.");
        }

        if (maxUsagePerUser <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUsagePerUser), "Max usage per user must be greater than 0.");
        }

        if (NormalizeUtc(endDate) <= NormalizeUtc(startDate))
        {
            throw new ArgumentException("End date must be after start date.", nameof(endDate));
        }
    }

    private static string NormalizeRequired(string value, string paramName) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{paramName} cannot be empty.", paramName) : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
