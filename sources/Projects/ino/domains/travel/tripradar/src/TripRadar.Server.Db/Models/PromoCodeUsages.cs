using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.PromoCodeUsages, Schema = DbConstants.SchemaName)]
public class PromoCodeUsages
{
    [Key] public long PromoCodeUsageId { get; set; }

    [Required] public long PromoCodeId { get; set; }

    [Required] public long UserId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime UsedAt { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Decimal18_2)] public decimal DiscountApplied { get; set; }

    [NotMapped] public PromoCodes? PromoCode { get; set; }

    [NotMapped] public Users? User { get; set; }
}
