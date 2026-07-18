using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Index(nameof(StripeIdHash))]
[Table(DbConstants.Tables.Prices, Schema = DbConstants.SchemaName)]
public class Prices
{
    [Key] public long PriceId { get; set; }

    [Required]
    public int TierId { get; set; }

    [ForeignKey("TierId")] public Tiers Tier { get; set; } = null!;

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Numeric.BigInt)]
    public long Amount { get; set; }

    [Required]
    public int BillingPeriodId { get; set; }

    [ForeignKey("BillingPeriodId")] public BillingPeriods BillingPeriod { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)] public string? StripeId { get; set; }

    [MaxLength(DbConstants.Validations.MaxLengths.L64)]
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar64)]
    public string? StripeIdHash { get; set; }

    [Required]
    public int CurrencyId { get; set; }

    [ForeignKey("CurrencyId")] public Currencies Currency { get; set; } = null!;

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime? UpdatedAt { get; set; }
}
