using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.PromoCodes, Schema = DbConstants.SchemaName)]
public class PromoCodes
{
    [Key] public long PromoCodeId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar50)] public string Code { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar500)] public string? Description { get; set; }

    public int DiscountTypeId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Decimal18_2)] public decimal DiscountValue { get; set; }

    public int? MaxUsageCount { get; set; }

    public int CurrentUsageCount { get; set; }

    public int MaxUsagePerUser { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTimeOffset StartDate { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTimeOffset EndDate { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)] public bool IsActive { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime CreatedAt { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)] public bool IsDeleted { get; set; }

    [NotMapped] public DiscountTypes? DiscountType { get; set; }

    [NotMapped] public ICollection<PromoCodeUsages> PromoCodeUsages { get; set; } = new List<PromoCodeUsages>();

    [NotMapped] public ICollection<Users> Users { get; set; } = new List<Users>();
}
