using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Index(nameof(UniqueId), IsUnique = true)]
[Index(nameof(OwnerId), nameof(CreatedOn), Name = "IX_TripVaults_OwnerId_CreatedOn", IsDescending = new[] { false, true })]
[Table(DbConstants.Tables.TripVaults, Schema = DbConstants.SchemaName)]
public class TripVaults
{
    [Key] public long TripVaultId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Identifier.Uuid)] public Guid UniqueId { get; set; }

    [Required] public long OwnerId { get; set; }

    [ForeignKey("OwnerId")] public Users Owner { get; set; } = null!;

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)]
    public string Name { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar500)]
    public string? Description { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime? StartDate { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime? EndDate { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime CreatedOn { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime? UpdatedOn { get; set; }

    [NotMapped]
    public ICollection<TripQueryHistories> QueryHistories { get; set; } = new List<TripQueryHistories>();
}
