using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.ServiceTypes, Schema = DbConstants.SchemaName)]
public class ServiceTypes
{
    [Key]
    public int ServiceTypeId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)]
    public string Name { get; set; } = null!;

    public int? PreferenceCategoryId { get; set; }

    [ForeignKey("PreferenceCategoryId")]
    public PreferenceCategories? PreferenceCategory { get; set; }

    [NotMapped]
    public ICollection<ServiceTokenCosts> ServiceTokenCosts { get; set; } = new List<ServiceTokenCosts>();
}
