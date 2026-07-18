using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Index(nameof(UserId), IsUnique = true, Name = "IX_UserSubscriptions_UserId")]
[Table(DbConstants.Tables.UserSubscriptions, Schema = DbConstants.SchemaName)]
public class UserSubscriptions
{
    [Key] public long UserSubscriptionId { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Numeric.BigInt)] public long UserId { get; set; }

    [ForeignKey("UserId")] public Users User { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)] public string? StripeCustomerId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)] public string? StripeSubscriptionId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime? SubscriptionExpirationTime { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)] public int? PendingTierId { get; set; }

    [ForeignKey("PendingTierId")] public Tiers? PendingTier { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)] public bool IsActive { get; set; } = true;

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)] public bool PayAsYouGoEnabled { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime CreatedAt { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)] public string? DeferredDowngradeJobId { get; set; }
}
