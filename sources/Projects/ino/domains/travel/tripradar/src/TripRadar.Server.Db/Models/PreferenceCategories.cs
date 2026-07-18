using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.PreferenceCategories, Schema = DbConstants.SchemaName)]
public class PreferenceCategories
{
    [Key]
    public int PreferenceCategoryId { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)]
    public string Name { get; set; } = null!;

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)]
    public bool IsActive { get; set; } = true;

    public ICollection<ServiceTypes> ServiceTypes { get; set; } = new List<ServiceTypes>();
}
