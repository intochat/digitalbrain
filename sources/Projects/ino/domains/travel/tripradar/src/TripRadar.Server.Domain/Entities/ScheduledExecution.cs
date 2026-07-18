using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class ScheduledExecution : Entity<long>
{
    private ScheduledExecution()
    {
    }

    public ScheduledExecution(
        long userId,
        string name,
        DateTime nextExecutionTime,
        string schedule,
        long? tripVaultId = null)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId), "User id must be greater than 0.");
        }

        UserId = userId;
        Name = NormalizeRequired(name, nameof(name));
        NextExecutionTime = NormalizeUtc(nextExecutionTime);
        Schedule = NormalizeRequired(schedule, nameof(schedule));
        TripVaultId = tripVaultId;
    }

    public new long Id { get; private set; }

    public Guid UniqueId { get; private set; } = Guid.NewGuid();

    public User User { get; private set; } = null!;

    public long UserId { get; private set; }

    public string Name { get; private set; } = null!;

    public bool IsActive { get; private set; } = true;

    public DateTime NextExecutionTime { get; private set; }

    public string Schedule { get; private set; } = null!;

    public DateTime CreatedOn { get; private set; } = DateTime.UtcNow;

    public DateTime? UpdatedOn { get; private set; }

    public long? TripVaultId { get; private set; }

    public TripVault? TripVault { get; private set; }

    public void AttachUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        User = user;
    }

    public void UpdateNextExecutionTime(DateTime nextExecutionTime)
    {
        NextExecutionTime = NormalizeUtc(nextExecutionTime);
        UpdatedOn = DateTime.UtcNow;
    }

    public void UpdateActiveStatus(bool isActive)
    {
        IsActive = isActive;
        UpdatedOn = DateTime.UtcNow;
    }

    public void UpdateConfiguration(bool isActive, string schedule, DateTime nextExecutionTime)
    {
        IsActive = isActive;
        Schedule = NormalizeRequired(schedule, nameof(schedule));
        NextExecutionTime = NormalizeUtc(nextExecutionTime);
        UpdatedOn = DateTime.UtcNow;
    }

    private static string NormalizeRequired(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }

        return value.Trim();
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
