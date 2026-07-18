using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.SeedWork;
using ServiceType = TripRadar.Server.Domain.ReferenceData.ServiceType;

namespace TripRadar.Server.Domain.Entities;

public class PreferenceType : Entity<int>
{
    private PreferenceType()
    {
    }

    public PreferenceType(
        ServiceType serviceType,
        string name,
        PreferenceDataType dataType,
        string? validationSchema = null,
        bool isRequired = false,
        string? defaultValue = null)
    {
        ServiceType = serviceType;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
        ValidationSchema = validationSchema;
        IsRequired = isRequired;
        DefaultValue = defaultValue;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public int ServiceTypeId { get; private set; }

    public ServiceType ServiceType { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public PreferenceDataType DataType { get; private set; } = null!;

    public string? ValidationSchema { get; private set; }

    public bool IsRequired { get; private set; }

    public string? DefaultValue { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }
}
