using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.Countries, Schema = DbConstants.SchemaName)]
public class Countries
{
    [Key] public int CountryId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar2)] public string CountryCode { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string CountryName { get; set; } = null!;
}
