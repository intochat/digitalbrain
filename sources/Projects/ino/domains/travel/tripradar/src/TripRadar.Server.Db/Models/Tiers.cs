using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.Tiers, Schema = DbConstants.SchemaName)]
public class Tiers
{
    [Key] public int TierId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar50)] public string Name { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Decimal10_2)] public decimal TokensPerMonthLimit { get; set; }

    [NotMapped] public ICollection<Users> Users { get; set; } = new List<Users>();
}
