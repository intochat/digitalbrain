using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.PreferenceTypes, Schema = DbConstants.SchemaName)]
public class PreferenceTypes
{
    [Key]
    public int PreferenceTypeId { get; set; }

    [Required]
    public int ServiceTypeId { get; set; }

    [ForeignKey("ServiceTypeId")]
    public ServiceTypes ServiceTypes { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string DataType { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Json.Jsonb)]
    public string? ValidationSchema { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)]
    public bool IsRequired { get; set; }

    [MaxLength(500)]
    public string? DefaultValue { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)]
    public bool IsActive { get; set; } = true;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UserPreferences> UserPreferences { get; set; } = new List<UserPreferences>();
}
