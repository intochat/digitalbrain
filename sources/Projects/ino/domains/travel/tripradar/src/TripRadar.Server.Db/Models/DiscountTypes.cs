using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.DiscountTypes, Schema = DbConstants.SchemaName)]
public class DiscountTypes
{
    [Key] public int DiscountTypeId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar50)] public string Name { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar200)] public string? Description { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime CreatedAt { get; set; }

    public bool IsDeleted { get; set; }

    [NotMapped] public ICollection<PromoCodes> PromoCodes { get; set; } = new List<PromoCodes>();
}
