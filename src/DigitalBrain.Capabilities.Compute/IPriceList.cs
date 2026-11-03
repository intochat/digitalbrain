namespace DigitalBrain.Capabilities.Compute;

// Modules price what they own. Micros per one unit at list price, or null when unpriced here.
public interface IPriceList
{
    long? MicrosPerUnit(CostKind kind, string item);

    PriceQuote Quote(CostKind kind, string item, string unit) => new()
    {
        Unit = unit,
        MicrosPerUnit = MicrosPerUnit(kind, item),
        ChargePolicy = ExternalChargePolicy.Unspecified,
    };
}
