using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.UsageEventSources, Schema = DbConstants.SchemaName)]
public class UsageEventSources
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int UsageEventSourceId { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar50)]
    public string Name { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar200)]
    public string? Description { get; set; }

    [Required]
    public bool IsActive { get; set; } = true;
}
