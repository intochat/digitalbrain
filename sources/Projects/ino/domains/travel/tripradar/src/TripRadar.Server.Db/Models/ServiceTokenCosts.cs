using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.ServiceTokenCosts, Schema = DbConstants.SchemaName)]
public class ServiceTokenCosts
{
    [Key] public int ServiceTokenCostId { get; set; }

    [Required]
    public int ServiceTypeId { get; set; }

    [ForeignKey("ServiceTypeId")] public ServiceTypes ServiceType { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Decimal10_2)] public decimal Cost { get; set; }
}
