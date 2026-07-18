using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.OverageBillingRecords, Schema = DbConstants.SchemaName)]
public class OverageBillingRecords
{
    [Key] public long OverageBillingRecordId { get; set; }

    [Required]
    [ForeignKey("User")]
    public long UserId { get; set; }

    public Users User { get; set; } = null!;

    [Required]
    [ForeignKey("ServiceType")]
    public int ServiceTypeId { get; set; }

    public ServiceTypes ServiceType { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Decimal18_6)] public decimal? OverageTokensUsed { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Decimal18_6)]
    public decimal TokenUnitCost { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Decimal18_2)]
    public decimal TotalCharge { get; set; }

    [Required]
    [ForeignKey("Currency")]
    public int CurrencyId { get; set; }

    public Currencies Currency { get; set; } = null!;

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)]
    public int Year { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)]
    public int Month { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime UsageTimestamp { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)]
    public bool IsBilled { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime? BilledAt { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)] public string? StripeInvoiceId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)] public string? Metadata { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Used to prevent race conditions in billing. Set when a process is actively billing these records.
    /// </summary>
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)]
    public string? ProcessingId { get; set; }

    /// <summary>
    /// Timestamp when processing started. Used to detect stale locks.
    /// </summary>
    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime? ProcessingStartedAt { get; set; }
}
