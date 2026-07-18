using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.BillingPeriods, Schema = DbConstants.SchemaName)]
public class BillingPeriods
{
    [Key] public int BillingPeriodId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar50)] public string Name { get; set; } = null!;

    [NotMapped] public ICollection<Prices> Prices { get; set; } = new List<Prices>();
}
