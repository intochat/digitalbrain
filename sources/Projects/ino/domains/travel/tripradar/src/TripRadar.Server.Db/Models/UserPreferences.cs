using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.UserPreferences, Schema = DbConstants.SchemaName)]
public class UserPreferences
{
    [Key]
    public long UserPreferenceId { get; set; }

    [Required]
    public long UserId { get; set; }

    [ForeignKey("UserId")]
    public Users Users { get; set; } = null!;

    [Required]
    public int PreferenceTypeId { get; set; }

    [ForeignKey("PreferenceTypeId")]
    public PreferenceTypes PreferenceTypes { get; set; } = null!;

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Json.Jsonb)]
    public string Value { get; set; } = null!;

    [Required]
    public bool IsActive { get; set; } = true;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
