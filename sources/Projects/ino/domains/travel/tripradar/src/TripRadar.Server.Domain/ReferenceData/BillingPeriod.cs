namespace TripRadar.Server.Domain.ReferenceData;

public class BillingPeriod
{
    private BillingPeriod()
    {
    }

    public BillingPeriod(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Billing period name is required", nameof(name));
        }

        Name = name.Trim();
    }

    public string Name { get; } = null!;
}
