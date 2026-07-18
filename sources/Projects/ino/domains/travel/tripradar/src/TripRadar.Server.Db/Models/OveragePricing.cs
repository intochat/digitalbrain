using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.OveragePricing, Schema = DbConstants.SchemaName)]
public class OveragePricing
{
    [Key] public int OveragePricingId { get; set; }

    [Required]
    public int TierId { get; set; }

    [ForeignKey("TierId")] public Tiers Tier { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Decimal18_6)] public decimal PricePerToken { get; set; }

    [Required]
    public int CurrencyId { get; set; }

    [ForeignKey("CurrencyId")] public Currencies Currency { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
