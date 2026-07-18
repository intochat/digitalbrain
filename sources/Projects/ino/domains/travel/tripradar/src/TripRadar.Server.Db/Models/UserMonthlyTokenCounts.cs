using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.UserMonthlyTokenCounts, Schema = DbConstants.SchemaName)]
public class UserMonthlyTokenCounts
{
    [Key] public long UserMonthlyTokenCountId { get; set; }

    [Required] public long UserId { get; set; }

    [ForeignKey("UserId")] public Users User { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Decimal10_2)] public decimal TokensConsumed { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Decimal10_2)] public decimal OverageTokensConsumed { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)] public int Year { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)] public int Month { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar50)] public string TimeZone { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime CreatedAt { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime LastUpdateTime { get; set; }
}
