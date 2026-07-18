using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.Currencies, Schema = DbConstants.SchemaName)]
public class Currencies
{
    [Key] public int CurrencyId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar10)] public string CurrencyCode { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string CurrencyName { get; set; } = null!;
}
