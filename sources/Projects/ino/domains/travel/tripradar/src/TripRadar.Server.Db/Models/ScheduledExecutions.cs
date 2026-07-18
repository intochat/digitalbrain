using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Index(nameof(UniqueId), IsUnique = true)]
[Index(nameof(UserId), nameof(CreatedOn), Name = "IX_ScheduledExecutions_UserId_CreatedOn", IsDescending = new[] { false, true })]
[Index(nameof(IsActive), nameof(NextExecutionTime), Name = "IX_ScheduledExecutions_IsActive_NextExecutionTime")]
[Table(DbConstants.Tables.ScheduledExecutions, Schema = DbConstants.SchemaName)]
public class ScheduledExecutions
{
    [Key] public long ScheduledExecutionId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Identifier.Uuid)] public Guid UniqueId { get; set; }

    [Required] public long UserId { get; set; }

    [ForeignKey("UserId")] public Users Users { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string Name { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)] public bool IsActive { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime NextExecutionTime { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string Schedule { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime CreatedOn { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime? UpdatedOn { get; set; }

    public long? TripVaultId { get; set; }

    [ForeignKey("TripVaultId")] public TripVaults? TripVault { get; set; }
}

