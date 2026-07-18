using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Index(nameof(UniqueId), IsUnique = true)]
[Index(nameof(ScheduledExecutionId), nameof(CreatedOn), Name = "IX_ScheduledLocalPlacesQueries_ScheduledExecutionId_CreatedOn", IsDescending = new[] { false, true })]
[Table(DbConstants.Tables.ScheduledLocalPlacesQueries, Schema = DbConstants.SchemaName)]
public class ScheduledLocalPlacesQueries
{
    [Key] public long ScheduledLocalPlacesQueryId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Identifier.Uuid)] public Guid UniqueId { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar500)]
    public string SearchQuery { get; set; } = null!;

    public long? ScheduledExecutionId { get; set; }

    [ForeignKey("ScheduledExecutionId")] public ScheduledExecutions? ScheduledExecution { get; set; }

    [Required] public long UserId { get; set; }

    [ForeignKey("UserId")] public Users Users { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime CreatedOn { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime? UpdatedOn { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Json.Jsonb)] public string? AdditionalParameters { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Json.Jsonb)] public string? SelectedColumns { get; set; }
}
